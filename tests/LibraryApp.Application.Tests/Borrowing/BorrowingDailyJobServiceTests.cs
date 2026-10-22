using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Borrowing;

public sealed class BorrowingDailyJobServiceTests
{
    // 20 October at 12:00 in Yerevan.
    private static readonly DateTime DueUtc =
        new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private readonly Guid _staffId = Guid.NewGuid();
    private readonly Guid _bookId = Guid.NewGuid();
    private readonly Guid _copyId = Guid.NewGuid();

    private readonly FakeLoanRepository _loans = new();
    private readonly InMemoryCatalogService _catalog = new();
    private readonly FakeEventBus _events = new();
    private readonly FakeApplicationLock _locks = new();
    private readonly ReservationRepositoryFake _reservations = new();
    private readonly FixedYerevanClock _clock = new(DueUtc);
    private readonly StaffStatusesFake _staff = new();

    private readonly FakeUnitOfWork _unitOfWork;

    public BorrowingDailyJobServiceTests()
    {
        _unitOfWork = new FakeUnitOfWork(
            _loans,
            _catalog,
            _events);
    }

    private BorrowingDailyJobService Service(
        OverdueFromMode mode = OverdueFromMode.NextLocalDay)
    {
        var options = BorrowingTestSettings.Create(
            settings => settings.Loans.OverdueFrom = mode);

        return new BorrowingDailyJobService(
            _loans,
            _catalog,
            new UnusedPenaltyService(),
            new OverdueRule(_clock, options),
            _unitOfWork,
            _locks,
            _events,
            _clock,
            new FakeIdGenerator(),
            _reservations,
            new ReservationQueueService(
                _reservations,
                _catalog,
                _locks,
                _events,
                new WeekdayCalendar(),
                _clock,
                new FakeIdGenerator(),
                options),
            _staff);
    }

    [Fact]
    public async Task A_deleted_book_cancels_every_waiting_reservation_of_its_queue()
    {
        Reservation first = SeedWaitingReservation(1);
        Reservation second = SeedWaitingReservation(2);
        Guid otherBook = Guid.NewGuid();
        Reservation otherQueue = SeedWaitingReservation(1, otherBook);
        _catalog.SeedBook(_bookId, isDeleted: true);

        int cancelled = await Service().CancelWaitingReservationsOfDeletedBookAsync(_bookId);

        Assert.Equal(2, cancelled);
        Assert.All(new[] { first, second }, reservation =>
        {
            Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
            Assert.Equal(0, reservation.QueuePosition);
        });
        Assert.Equal((ReservationStatus.Waiting, 1), (otherQueue.Status, otherQueue.QueuePosition));
        Assert.Equal(new[] { $"lock:{BorrowingLocks.Book(_bookId)}" }, _locks.Log);
        Assert.Empty(_events.Published);
        Assert.Equal(1, _unitOfWork.CommittedCount);
    }

    [Fact]
    public async Task A_book_that_is_not_deleted_keeps_its_queue_and_takes_no_lock()
    {
        Reservation waiting = SeedWaitingReservation(1);
        _catalog.SeedBook(_bookId, isDeleted: false);

        Assert.Equal(0, await Service().CancelWaitingReservationsOfDeletedBookAsync(_bookId));

        Assert.Equal((ReservationStatus.Waiting, 1), (waiting.Status, waiting.QueuePosition));
        Assert.Empty(_locks.Log);
    }

    [Fact]
    public async Task A_book_unknown_to_catalog_is_treated_as_deleted_and_a_second_run_changes_nothing()
    {
        Reservation waiting = SeedWaitingReservation(1);
        _catalog.SeedBook(Guid.NewGuid(), isDeleted: false);   // the catalog knows other books, not this one

        BorrowingDailyJobService service = Service();

        Assert.Equal(1, await service.CancelWaitingReservationsOfDeletedBookAsync(_bookId));
        Assert.Equal(0, await service.CancelWaitingReservationsOfDeletedBookAsync(_bookId));
        Assert.Equal(ReservationStatus.Cancelled, waiting.Status);
    }

    [Fact]
    public async Task A_deactivated_member_leaves_every_queue_and_their_held_copy_goes_to_the_next_member()
    {
        Guid member = Guid.NewGuid();
        Guid secondBook = Guid.NewGuid();

        // Book A: the member waits first, someone waits behind them.
        Reservation waiting = SeedWaitingReservation(1, _bookId, member);
        Reservation behind = SeedWaitingReservation(2, _bookId);

        // Book B: a copy is held for the member, someone waits for it.
        Guid heldCopy = Guid.NewGuid();
        Reservation held = SeedReadyReservation(DueUtc.AddDays(2), secondBook, heldCopy, member);
        Reservation next = SeedWaitingReservation(1, secondBook);

        _staff.Statuses[member] = StaffMemberStatus.Deactivated;

        int cancelled = await Service().CancelReservationsOfDeactivatedMemberAsync(member);

        Assert.Equal(2, cancelled);
        Assert.Equal(ReservationStatus.Cancelled, waiting.Status);
        Assert.Equal((ReservationStatus.Waiting, 1), (behind.Status, behind.QueuePosition));
        Assert.Equal(ReservationStatus.Cancelled, held.Status);
        Assert.Equal((ReservationStatus.ReadyForPickup, (Guid?)heldCopy), (next.Status, next.CopyId));
        Assert.Equal(BookCopyStatus.Reserved, _catalog.Snapshot()[heldCopy].Status);

        ReservationReady ready = Assert.Single(_events.Published.OfType<ReservationReady>());
        Assert.Equal((next.Id, secondBook, next.StaffMemberId), (ready.ReservationId, ready.BookId, ready.StaffMemberId));

        // Staff first, then the books in book-id order (the order the repository returns them in).
        IEnumerable<string> bookLocks = new[] { _bookId, secondBook }
            .OrderBy(id => id)
            .Select(book => $"lock:{BorrowingLocks.Book(book)}");
        Assert.Equal(new[] { $"lock:{BorrowingLocks.Staff(member)}" }.Concat(bookLocks), _locks.Log);
    }

    [Fact]
    public async Task A_copy_held_for_a_deactivated_member_becomes_available_when_nobody_waits()
    {
        Guid member = Guid.NewGuid();
        Reservation held = SeedReadyReservation(DueUtc.AddDays(2), staffMemberId: member);
        _staff.Statuses[member] = StaffMemberStatus.Deactivated;

        BorrowingDailyJobService service = Service();

        Assert.Equal(1, await service.CancelReservationsOfDeactivatedMemberAsync(member));
        Assert.Equal(0, await service.CancelReservationsOfDeactivatedMemberAsync(member));

        Assert.Equal(ReservationStatus.Cancelled, held.Status);
        Assert.Equal(BookCopyStatus.Available, _catalog.Snapshot()[_copyId].Status);
        Assert.Empty(_events.Published.OfType<ReservationReady>());
    }

    [Theory]
    [InlineData(StaffMemberStatus.Active)]
    [InlineData(StaffMemberStatus.Restricted)]
    [InlineData(StaffMemberStatus.Suspended)]
    public async Task A_member_who_is_not_deactivated_keeps_their_reservations(StaffMemberStatus status)
    {
        Guid member = Guid.NewGuid();
        Reservation waiting = SeedWaitingReservation(1, _bookId, member);
        Reservation held = SeedReadyReservation(DueUtc.AddDays(2), Guid.NewGuid(), Guid.NewGuid(), member);
        _staff.Statuses[member] = status;

        Assert.Equal(0, await Service().CancelReservationsOfDeactivatedMemberAsync(member));

        Assert.Equal(ReservationStatus.Waiting, waiting.Status);
        Assert.Equal(ReservationStatus.ReadyForPickup, held.Status);
        Assert.Empty(_locks.Log);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task A_reservation_closed_after_the_member_was_read_is_skipped_under_its_book_lock()
    {
        Guid member = Guid.NewGuid();
        Reservation waiting = SeedWaitingReservation(1, _bookId, member);
        _staff.Statuses[member] = StaffMemberStatus.Deactivated;

        // The member's list is read before the book lock; a parallel operation closes the reservation meanwhile.
        _reservations.BeforeGetForUpdate = reservation => reservation.Cancel();

        Assert.Equal(0, await Service().CancelReservationsOfDeactivatedMemberAsync(member));
        Assert.Equal(ReservationStatus.Cancelled, waiting.Status);
        Assert.Equal(0, _reservations.FlushCount);
    }

    [Fact]
    public async Task Pickup_deadline_equal_to_now_is_not_expired()
    {
        Reservation reservation = SeedReadyReservation(DueUtc);
        _clock.UtcNow = DueUtc;

        bool changed = await Service().ExpirePickupAsync(reservation.Id);

        Assert.False(changed);
        Assert.Equal(ReservationStatus.ReadyForPickup, reservation.Status);
        Assert.Empty(_events.Published.OfType<ReservationReady>());
        Assert.Equal(0, _catalog.SetStatusCalls);
    }

    [Fact]
    public async Task An_expired_pickup_is_offered_to_the_next_waiter_only_once()
    {
        Reservation expired = SeedReadyReservation(DueUtc.AddMinutes(-1));
        Reservation next = SeedWaitingReservation(1);
        _clock.UtcNow = DueUtc;

        BorrowingDailyJobService service = Service();

        Assert.True(await service.ExpirePickupAsync(expired.Id));
        Assert.False(await service.ExpirePickupAsync(expired.Id));

        Assert.Equal(ReservationStatus.Expired, expired.Status);
        Assert.Equal(_copyId, expired.CopyId);
        Assert.Equal(ReservationStatus.ReadyForPickup, next.Status);
        Assert.Equal(_copyId, next.CopyId);
        Assert.Equal(BookCopyStatus.Reserved, _catalog.Snapshot()[_copyId].Status);
        Assert.Single(_events.Published.OfType<ReservationReady>());
        Assert.Equal(1, _reservations.FlushCount);
    }

    [Fact]
    public async Task An_expired_pickup_makes_the_copy_available_when_queue_is_empty()
    {
        Reservation expired = SeedReadyReservation(DueUtc.AddMinutes(-1));
        _clock.UtcNow = DueUtc;

        bool changed = await Service().ExpirePickupAsync(expired.Id);

        Assert.True(changed);
        Assert.Equal(ReservationStatus.Expired, expired.Status);
        Assert.Equal(BookCopyStatus.Available, _catalog.Snapshot()[_copyId].Status);
        Assert.Empty(_events.Published.OfType<ReservationReady>());
    }

    [Theory]
    // Held on Friday 16 October: 3 working days (BOR-07) end on Wednesday 21 October at local midnight.
    [InlineData("2026-10-21T23:59:00", false)]   // the last minute of the third working day
    [InlineData("2026-10-22T00:30:00", true)]    // the next daily job (BOR-06, 00:30)
    public async Task Pickup_held_on_friday_expires_at_the_first_daily_job_after_the_third_working_day(
        string localRunTime,
        bool expired)
    {
        var holdClock = new FixedYerevanClock(new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc));
        DateTime deadline = ReservationQueueService.ReadyUntilUtc(
            BorrowingTestSettings.Defaults().Reservations,
            new WeekdayCalendar(),
            holdClock);
        Assert.Equal(new DateTime(2026, 10, 21, 20, 0, 0, DateTimeKind.Utc).AddTicks(-1), deadline);

        Reservation reservation = SeedReadyReservation(deadline);
        // Asia/Yerevan is UTC+4 all year.
        _clock.UtcNow = DateTime.SpecifyKind(
            DateTime.Parse(localRunTime, System.Globalization.CultureInfo.InvariantCulture).AddHours(-4),
            DateTimeKind.Utc);

        bool changed = await Service().ExpirePickupAsync(reservation.Id);

        Assert.Equal(expired, changed);
        Assert.Equal(
            expired ? ReservationStatus.Expired : ReservationStatus.ReadyForPickup,
            reservation.Status);
    }

    [Fact]
    public async Task Expired_pickup_goes_to_the_first_waiter_with_a_new_deadline_under_the_book_lock()
    {
        Reservation expired = SeedReadyReservation(DueUtc.AddMinutes(-1));
        Reservation first = SeedWaitingReservation(1);
        Reservation second = SeedWaitingReservation(2);
        Reservation third = SeedWaitingReservation(3);
        _clock.UtcNow = DueUtc;

        Assert.True(await Service().ExpirePickupAsync(expired.Id));

        Assert.Equal(new[] { $"lock:{BorrowingLocks.Book(_bookId)}" }, _locks.Log);
        Assert.Equal(0, expired.QueuePosition);

        // Tuesday 20 October + 3 working days → the end of Friday 23 October, local time (BOR-07).
        DateTime newDeadline = new DateTime(2026, 10, 23, 20, 0, 0, DateTimeKind.Utc).AddTicks(-1);
        Assert.Equal(ReservationStatus.ReadyForPickup, first.Status);
        Assert.Equal(newDeadline, first.ReadyUntilUtc);
        Assert.Equal((1, 2), (second.QueuePosition, third.QueuePosition));

        // One event, for the next member only: no message about the expiry itself.
        var ready = Assert.IsType<ReservationReady>(Assert.Single(_events.Published));
        Assert.Equal(first.Id, ready.ReservationId);
        Assert.Equal(_bookId, ready.BookId);
        Assert.Equal(first.StaffMemberId, ready.StaffMemberId);
        Assert.Equal(newDeadline, ready.ReadyUntilUtc);
        Assert.Equal(DueUtc, ready.OccurredAtUtc);
        // The copy stays Reserved: no status change is needed when it passes to the next member.
        Assert.Equal(0, _catalog.SetStatusCalls);
    }

    [Theory]
    [InlineData("fulfilled")]
    [InlineData("cancelled")]
    [InlineData("unknown")]
    public async Task Pickup_closed_by_another_request_first_is_left_unchanged(string closedBy)
    {
        Reservation reservation = SeedReadyReservation(DueUtc.AddMinutes(-1));
        SeedWaitingReservation(1);
        _clock.UtcNow = DueUtc;

        Guid id = reservation.Id;
        switch (closedBy)
        {
            case "fulfilled": reservation.Fulfil(); break;   // the member collected the copy before the job
            case "cancelled": reservation.Cancel(); break;   // a librarian cancelled it before the job (BOR-12)
            default: id = Guid.NewGuid(); break;
        }

        ReservationStatus before = reservation.Status;

        Assert.False(await Service().ExpirePickupAsync(id));

        Assert.Equal(before, reservation.Status);
        Assert.Equal(0, _catalog.SetStatusCalls);
        Assert.Empty(_events.Published);
        Assert.Equal(0, _reservations.FlushCount);
    }

    private Reservation SeedReadyReservation(
        DateTime readyUntilUtc,
        Guid? bookId = null,
        Guid? copyId = null,
        Guid? staffMemberId = null)
    {
        Guid book = bookId ?? _bookId;
        Guid copy = copyId ?? _copyId;

        _catalog.Seed(new BookCopyDto(
            copy,
            book,
            copy == _copyId ? "DAILY-001" : $"DAILY-{copy:N}",
            BookCopyStatus.Reserved,
            BookCopyCondition.Good,
            "A-1"));

        Reservation reservation = Reservation.Create(
            Guid.NewGuid(), book, staffMemberId ?? Guid.NewGuid(), DueUtc.AddDays(-10), 1);
        reservation.MarkReady(copy, readyUntilUtc);
        _reservations.All.Add(reservation);
        return reservation;
    }

    private Reservation SeedWaitingReservation(
        int position,
        Guid? bookId = null,
        Guid? staffMemberId = null)
    {
        Reservation reservation = Reservation.Create(
            Guid.NewGuid(), bookId ?? _bookId, staffMemberId ?? Guid.NewGuid(), DueUtc.AddDays(-5).AddMinutes(position), position);
        _reservations.All.Add(reservation);
        return reservation;
    }

    private Loan SeedLoan(
        bool includeCopy = true,
        DateTime? dueAtUtc = null)
    {
        DateTime due = dueAtUtc ?? DueUtc;

        Loan loan = Loan.Issue(
            Guid.NewGuid(),
            _copyId,
            _staffId,
            due.AddDays(-1),
            loanDays: 1);

        _loans.Seed(loan);

        if (includeCopy)
        {
            _catalog.Seed(new BookCopyDto(
                _copyId,
                _bookId,
                "DAILY-001",
                BookCopyStatus.OnLoan,
                BookCopyCondition.Good,
                "A-1"));
        }

        return loan;
    }

    [Theory]
    [InlineData(19, 59, 59, false)]
    [InlineData(20, 0, 0, true)]
    public async Task NextLocalDay_uses_Yerevan_midnight(
        int utcHour,
        int utcMinute,
        int utcSecond,
        bool expectedChange)
    {
        Loan loan = SeedLoan();

        // 19:59:59 UTC = 23:59:59 on the local due date.
        // 20:00:00 UTC = 00:00:00 on the next local date.
        _clock.UtcNow = new DateTime(
            2026, 10, 20,
            utcHour, utcMinute, utcSecond,
            DateTimeKind.Utc);

        bool changed = await Service().MarkLoanOverdueAsync(loan.Id);

        Assert.Equal(expectedChange, changed);

        Assert.Equal(
            expectedChange ? LoanStatus.Overdue : LoanStatus.Active,
            loan.Status);

        Assert.Equal(
            expectedChange ? 1 : 0,
            _events.Published.OfType<LoanOverdue>().Count());
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task DueTime_requires_the_exact_deadline_to_have_passed(
        int ticksAfterDue,
        bool expectedChange)
    {
        Loan loan = SeedLoan();
        _clock.UtcNow = DueUtc.AddTicks(ticksAfterDue);

        bool changed = await Service(OverdueFromMode.DueTime)
            .MarkLoanOverdueAsync(loan.Id);

        Assert.Equal(expectedChange, changed);

        Assert.Equal(
            expectedChange ? LoanStatus.Overdue : LoanStatus.Active,
            loan.Status);
    }

    [Fact]
    public async Task Repeated_processing_publishes_one_correct_event()
    {
        Loan loan = SeedLoan();
        _clock.UtcNow = DueUtc.AddDays(1);

        BorrowingDailyJobService service = Service();

        Assert.True(await service.MarkLoanOverdueAsync(loan.Id));
        Assert.False(await service.MarkLoanOverdueAsync(loan.Id));

        var overdue = Assert.Single(
            _events.Published.OfType<LoanOverdue>());

        Assert.Equal(loan.Id, overdue.LoanId);
        Assert.Equal(_staffId, overdue.StaffMemberId);
        Assert.Equal(_bookId, overdue.BookId);
        Assert.Equal(DueUtc, overdue.DueAtUtc);
        Assert.Equal(_clock.UtcNow, overdue.OccurredAtUtc);
        Assert.NotEqual(Guid.Empty, overdue.EventId);

        Assert.Equal(LoanStatus.Overdue, loan.Status);

        // Becoming overdue does not change the physical copy's status.
        Assert.Equal(0, _catalog.SetStatusCalls);

        // Each call owns its own transaction, including the no-op call.
        Assert.Equal(2, _unitOfWork.CommittedCount);
    }

    [Fact]
    public async Task Locks_are_acquired_staff_first_then_book()
    {
        Loan loan = SeedLoan();
        _clock.UtcNow = DueUtc.AddDays(1);

        await Service().MarkLoanOverdueAsync(loan.Id);

        Assert.Equal(
            new[]
            {
                $"lock:{BorrowingLocks.Staff(_staffId)}",
                $"lock:{BorrowingLocks.Book(_bookId)}"
            },
            _locks.Log);
    }

    [Theory]
    [InlineData(LoanStatus.Returned)]
    [InlineData(LoanStatus.Lost)]
    [InlineData(LoanStatus.Overdue)]
    public async Task A_loan_that_is_no_longer_Active_is_skipped(
        LoanStatus status)
    {
        Loan loan = SeedLoan();
        _clock.UtcNow = DueUtc.AddDays(1);

        switch (status)
        {
            case LoanStatus.Returned:
                loan.Return(DueUtc);
                break;

            case LoanStatus.Lost:
                loan.MarkLost(DueUtc);
                break;

            case LoanStatus.Overdue:
                loan.MarkOverdue(_clock.UtcNow);
                break;
        }

        bool changed = await Service().MarkLoanOverdueAsync(loan.Id);

        Assert.False(changed);
        Assert.Equal(status, loan.Status);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task A_renewed_loan_is_checked_again_and_skipped()
    {
        Loan loan = SeedLoan();
        _clock.UtcNow = DueUtc.AddDays(1);

        // Represents a renewal after candidate selection,
        // but before the worker processes this loan.
        loan.Renew(7);

        bool changed = await Service().MarkLoanOverdueAsync(loan.Id);

        Assert.False(changed);
        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task An_unknown_loan_is_skipped()
    {
        bool changed = await Service()
            .MarkLoanOverdueAsync(Guid.NewGuid());

        Assert.False(changed);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task A_missing_copy_is_reported_without_marking_the_loan()
    {
        Loan loan = SeedLoan(includeCopy: false);
        _clock.UtcNow = DueUtc.AddDays(1);

        DomainException exception =
            await Assert.ThrowsAsync<DomainException>(
                () => Service().MarkLoanOverdueAsync(loan.Id));

        Assert.Equal(
            BorrowingErrors.CopyNotFound.Code,
            exception.Error.Code);

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Empty(_events.Published);
        Assert.Equal(1, _unitOfWork.RolledBackCount);
    }

    [Theory]
    [InlineData(2026, 10, 18)] // Sunday.
    [InlineData(2026, 1, 1)]   // Holiday in the supplied configuration.
    public async Task Overdue_marking_does_not_wait_for_a_working_day(
        int year,
        int month,
        int day)
    {
        var localRunDate = new DateOnly(year, month, day);

        _clock.UtcNow = _clock
            .StartOfLocalDayUtc(localRunDate)
            .AddMinutes(30);

        DateTime dueYesterdayUtc = _clock
            .StartOfLocalDayUtc(localRunDate.AddDays(-1))
            .AddHours(12);

        Loan loan = SeedLoan(dueAtUtc: dueYesterdayUtc);

        bool changed = await Service().MarkLoanOverdueAsync(loan.Id);

        Assert.True(changed);
        Assert.Equal(LoanStatus.Overdue, loan.Status);
        Assert.Single(_events.Published.OfType<LoanOverdue>());
    }

    private sealed class UnusedPenaltyService : IPenaltyService
    {
        public Task<PenaltyApplicationResult> ApplyAsync(
            Loan loan,
            IReadOnlyList<ReturnPenaltyItem> items,
            DateTime nowUtc,
            CancellationToken ct = default) =>
            throw new NotSupportedException(
                "Overdue marking must not apply penalty points.");

        public Task<PenaltyRecalculationResult> RecalculateStatusAsync(
            Guid staffMemberId,
            DateTime nowUtc,
            CancellationToken ct = default) =>
            throw new NotSupportedException(
                "Loan processing must not recalculate staff penalties.");
    }

    private sealed class ReservationRepositoryFake : IReservationRepository
    {
        public List<Reservation> All { get; } = [];
        public int FlushCount { get; private set; }

        public Task<IReadOnlyList<Reservation>> GetWaitingForBookAsync(
            Guid bookId,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Reservation>>(
                All.Where(r => r.BookId == bookId && r.Status == ReservationStatus.Waiting)
                    .OrderBy(r => r.QueuePosition)
                    .ThenBy(r => r.CreatedAtUtc)
                    .ThenBy(r => r.Id)
                    .ToArray());

        public Task<Reservation?> FindAsync(Guid reservationId, CancellationToken ct = default) =>
            Task.FromResult(All.SingleOrDefault(r => r.Id == reservationId));

        /// <summary>Runs before a reservation is loaded for update: simulates a change made by a parallel operation.</summary>
        public Action<Reservation>? BeforeGetForUpdate { get; set; }

        public Task<Reservation?> GetForUpdateAsync(Guid reservationId, CancellationToken ct = default)
        {
            Reservation? reservation = All.SingleOrDefault(r => r.Id == reservationId);
            if (reservation is not null)
                BeforeGetForUpdate?.Invoke(reservation);
            return Task.FromResult(reservation);
        }

        public Task<IReadOnlyList<Reservation>> FindActiveForStaffAsync(Guid staffMemberId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Reservation>>(
                All.Where(r => r.StaffMemberId == staffMemberId
                               && r.Status is ReservationStatus.Waiting or ReservationStatus.ReadyForPickup)
                    .OrderBy(r => r.BookId)
                    .ToArray());

        public Task FlushAsync(CancellationToken ct = default)
        {
            FlushCount++;
            return Task.CompletedTask;
        }

        public Task<int> CountActiveAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasActiveForBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountWaitingForBookAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Reservation?> FindReadyForCopyAsync(Guid copyId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Reservation?> FindActiveForStaffAndBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Reservation reservation, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Staff statuses for the deactivated-member step; a member not listed is Active.</summary>
    private sealed class StaffStatusesFake : IStaffMemberService
    {
        public Dictionary<Guid, StaffMemberStatus> Statuses { get; } = new();

        public Task<StaffMemberStatus> GetStatusAsync(Guid staffMemberId, CancellationToken ct = default) =>
            Task.FromResult(Statuses.GetValueOrDefault(staffMemberId, StaffMemberStatus.Active));

        public Task<EligibilityResult> CanBorrowAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LendingRules> GetLendingRulesAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetStatusAsync(Guid staffMemberId, StaffMemberStatus status, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> staffMemberIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffMemberLookup?> FindAsync(string employeeIdOrEmail, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, StaffContact>> GetContactsAsync(IReadOnlyCollection<Guid> staffMemberIds, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class WeekdayCalendar : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        public DateOnly AddWorkingDays(DateOnly start, int workingDays)
        {
            DateOnly date = start;
            while (workingDays > 0)
            {
                date = date.AddDays(1);
                if (IsWorkingDay(date)) workingDays--;
            }
            return date;
        }
        public int CountWorkingDays(DateOnly fromExclusive, DateOnly toInclusive) => throw new NotSupportedException();
    }
}
