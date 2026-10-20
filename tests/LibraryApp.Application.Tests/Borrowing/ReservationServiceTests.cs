using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Borrowing;

public sealed class ReservationServiceTests
{
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateAsync_creates_waiting_reservation_at_end_of_queue_in_one_transaction()
    {
        var fixture = new Fixture { WaitingCount = 4 };
        var otherCopyId = Guid.NewGuid();
        fixture.Loans.CopyIds.Add(otherCopyId);
        fixture.Catalog.Copies[otherCopyId] = Copy(otherCopyId, Guid.NewGuid());

        ReservationDto result = await fixture.Service.CreateAsync(new CreateReservationRequest(BookId, StaffId));

        Assert.Equal(fixture.Ids.Id, result.ReservationId);
        Assert.Equal(BookId, result.BookId);
        Assert.Equal(StaffId, result.StaffMemberId);
        Assert.Equal(Now, result.CreatedAtUtc);
        Assert.Equal(ReservationStatus.Waiting, result.Status);
        Assert.Equal(5, result.QueuePosition);
        Assert.Null(result.CopyId);
        Assert.Null(result.ReadyUntilUtc);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        Assert.Equal(0, fixture.UnitOfWork.RollbackCount);
        // The shared staff lock (issuance and deactivation take it too), then the queue of the book: BorrowingLocks order.
        Assert.Equal(new[] { BorrowingLocks.Staff(StaffId), BorrowingLocks.Book(BookId) }, fixture.Lock.Resources);
        Assert.Equal(new[] { otherCopyId }, fixture.Catalog.LastResolvedCopyIds);
        Assert.Single(fixture.Reservations.Saved);
    }

    [Fact]
    public async Task CreateAsync_rejects_unknown_book()
    {
        var fixture = new Fixture();
        fixture.Catalog.Book = null;

        await AssertRejected(fixture, CatalogErrors.BookNotFound);
        Assert.Empty(fixture.Lock.Resources);   // checked before any lock is taken
    }

    [Fact]
    public async Task CreateAsync_rejects_soft_deleted_book()
    {
        var fixture = new Fixture();
        fixture.Catalog.Book = Book(isDeleted: true);

        // A deleted book is gone for readers: the same 404 as an unknown book.
        await AssertRejected(fixture, CatalogErrors.BookNotFound);
    }

    [Fact]
    public async Task CreateAsync_rejects_when_an_available_copy_exists()
    {
        var fixture = new Fixture();
        fixture.Catalog.AvailableCopy = Copy(Guid.NewGuid(), BookId);

        await AssertRejected(fixture, BorrowingErrors.ReservationCopyAvailable);
    }

    [Theory]
    [InlineData(StaffMemberStatus.Restricted, "borrowing.restricted_reservation_forbidden")]
    [InlineData(StaffMemberStatus.Suspended, "staff.suspended")]
    [InlineData(StaffMemberStatus.Deactivated, "staff.deactivated")]
    public async Task CreateAsync_rejects_ineligible_employee(StaffMemberStatus status, string expectedError)
    {
        var fixture = new Fixture { Status = status };

        DomainException exception = await Assert.ThrowsAsync<DomainException>(
            () => fixture.Service.CreateAsync(new CreateReservationRequest(BookId, StaffId)));

        Assert.Equal(expectedError, exception.Error.Code);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
        Assert.Equal(1, fixture.UnitOfWork.RollbackCount);
        Assert.Empty(fixture.Reservations.Saved);
    }

    [Fact]
    public async Task CreateAsync_rejects_employee_with_three_active_reservations()
    {
        var fixture = new Fixture { ActiveCount = 3 };

        await AssertRejected(fixture, BorrowingErrors.ReservationLimitReached(3));
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(5, 4, true)]
    [InlineData(5, 5, false)]
    public async Task CreateAsync_takes_the_reservation_limit_from_the_settings(int maxActive, int activeCount, bool allowed)
    {
        var fixture = new Fixture(settings => settings.Reservations.MaxActive = maxActive) { ActiveCount = activeCount };

        if (allowed)
            await fixture.Service.CreateAsync(new CreateReservationRequest(BookId, StaffId));
        else
            await AssertRejected(fixture, BorrowingErrors.ReservationLimitReached(maxActive));
    }

    [Fact]
    public async Task CreateAsync_lets_a_Restricted_employee_reserve_when_the_settings_allow_it()
    {
        var fixture = new Fixture(settings => settings.Reservations.RestrictedMayReserve = true)
        {
            Status = StaffMemberStatus.Restricted,
        };

        ReservationDto result = await fixture.Service.CreateAsync(new CreateReservationRequest(BookId, StaffId));

        Assert.Equal(ReservationStatus.Waiting, result.Status);
    }

    [Fact]
    public async Task CreateAsync_lets_a_Suspended_employee_reserve_when_the_settings_allow_it()
    {
        var fixture = new Fixture(settings => settings.Reservations.SuspendedMayReserve = true)
        {
            Status = StaffMemberStatus.Suspended,
        };

        ReservationDto result = await fixture.Service.CreateAsync(new CreateReservationRequest(BookId, StaffId));

        Assert.Equal(ReservationStatus.Waiting, result.Status);
    }

    [Fact]
    public async Task CreateAsync_still_refuses_a_Deactivated_employee_when_Suspended_may_reserve()
    {
        var fixture = new Fixture(settings =>
        {
            settings.Reservations.SuspendedMayReserve = true;
            settings.Reservations.RestrictedMayReserve = true;
        })
        {
            Status = StaffMemberStatus.Deactivated,
        };

        await AssertRejected(fixture, StaffErrors.Deactivated);
    }

    [Fact]
    public async Task CreateAsync_rejects_duplicate_active_reservation()
    {
        var fixture = new Fixture { HasActiveForBook = true };

        await AssertRejected(fixture, BorrowingErrors.DuplicateActiveReservation);
    }

    [Fact]
    public async Task CreateAsync_rejects_book_held_by_an_open_loan_using_catalog_copy_resolution()
    {
        var fixture = new Fixture();
        var copyId = Guid.NewGuid();
        fixture.Loans.CopyIds.Add(copyId);
        fixture.Catalog.Copies[copyId] = Copy(copyId, BookId);

        await AssertRejected(fixture, BorrowingErrors.BookAlreadyOnLoan);

        Assert.Equal(new[] { copyId }, fixture.Catalog.LastResolvedCopyIds);
    }

    [Fact]
    public async Task CreateAsync_reports_the_duplicate_before_the_limit_of_three()
    {
        var fixture = new Fixture { HasActiveForBook = true, ActiveCount = 3 };

        await AssertRejected(fixture, BorrowingErrors.DuplicateActiveReservation);
        Assert.Equal(ErrorKind.Conflict, BorrowingErrors.DuplicateActiveReservation.Kind);
    }

    [Fact]
    public async Task CreateAsync_reports_a_held_book_before_the_duplicate_and_the_limit()
    {
        var fixture = new Fixture { HasActiveForBook = true, ActiveCount = 3 };
        var copyId = Guid.NewGuid();
        fixture.Loans.CopyIds.Add(copyId);
        fixture.Catalog.Copies[copyId] = Copy(copyId, BookId);

        await AssertRejected(fixture, BorrowingErrors.BookAlreadyOnLoan);
    }

    [Fact]
    public async Task A_staff_member_reserves_for_themselves_without_naming_anyone()
    {
        var fixture = new Fixture();
        fixture.CurrentUser.Role = StaffRoles.StaffMember;
        fixture.CurrentUser.StaffMemberId = StaffId;

        ReservationDto result = await fixture.Service.CreateAsync(new CreateReservationRequest(BookId));

        Assert.Equal(StaffId, result.StaffMemberId);
    }

    [Fact]
    public async Task A_staff_member_cannot_reserve_for_someone_else()
    {
        var fixture = new Fixture();
        fixture.CurrentUser.Role = StaffRoles.StaffMember;
        fixture.CurrentUser.StaffMemberId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => fixture.Service.CreateAsync(new CreateReservationRequest(BookId, StaffId)));

        Assert.Equal(BorrowingErrors.ReservationForAnotherStaffMember, exception.Error);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount + fixture.UnitOfWork.RollbackCount);   // refused before the transaction
    }

    [Fact]
    public async Task A_librarian_must_name_the_staff_member()
    {
        var fixture = new Fixture();

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => fixture.Service.CreateAsync(new CreateReservationRequest(BookId)));

        Assert.Equal(BorrowingErrors.ReservationStaffMemberRequired, exception.Error);
    }

    [Fact]
    public async Task GetAsync_shows_a_staff_member_only_their_own_reservation()
    {
        var fixture = new Fixture();
        ReservationDto created = await fixture.Service.CreateAsync(new CreateReservationRequest(BookId, StaffId));

        Assert.Equal(created, await fixture.Service.GetAsync(created.ReservationId));   // Librarian

        fixture.CurrentUser.Role = StaffRoles.StaffMember;
        fixture.CurrentUser.StaffMemberId = StaffId;
        Assert.Equal(created, await fixture.Service.GetAsync(created.ReservationId));

        fixture.CurrentUser.StaffMemberId = Guid.NewGuid();
        var exception = await Assert.ThrowsAsync<DomainException>(() => fixture.Service.GetAsync(created.ReservationId));
        Assert.Equal(BorrowingErrors.ReservationNotFound, exception.Error);
    }

    [Fact]
    public async Task CancelAsync_cancels_a_waiting_reservation_and_compacts_the_queue()
    {
        var fixture = new Fixture();
        var first = fixture.AddWaiting(1);
        var cancelled = fixture.AddWaiting(2);
        var third = fixture.AddWaiting(3);
        fixture.CurrentUser.Role = StaffRoles.StaffMember;
        fixture.CurrentUser.StaffMemberId = cancelled.StaffMemberId;

        ReservationDto result = await fixture.Service.CancelAsync(cancelled.Id);

        Assert.Equal(ReservationStatus.Cancelled, result.Status);
        Assert.Equal(0, result.QueuePosition);
        Assert.Equal(1, first.QueuePosition);
        Assert.Equal(0, cancelled.QueuePosition);
        Assert.Equal(2, third.QueuePosition);
        Assert.Equal(1, fixture.UnitOfWork.CommitCount);
        // Staff, then book: the order of issue, return and reserve, so the operations cannot deadlock (SD-10).
        Assert.Equal(new[] { BorrowingLocks.Staff(cancelled.StaffMemberId), BorrowingLocks.Book(BookId) }, fixture.Lock.Resources);
    }

    [Fact]
    public async Task CancelAsync_renumbers_a_queue_with_gaps_to_one_to_n()
    {
        var fixture = new Fixture();
        var first = fixture.AddWaiting(2);
        var cancelled = fixture.AddWaiting(4);
        var third = fixture.AddWaiting(7);

        await fixture.Service.CancelAsync(cancelled.Id);

        Assert.Equal((1, 2), (first.QueuePosition, third.QueuePosition));
    }

    [Fact]
    public async Task CancelAsync_hides_another_members_reservation_as_not_found()
    {
        var fixture = new Fixture();
        var reservation = fixture.AddWaiting(1);
        fixture.CurrentUser.Role = StaffRoles.StaffMember;
        fixture.CurrentUser.StaffMemberId = Guid.NewGuid();

        DomainException exception = await Assert.ThrowsAsync<DomainException>(
            () => fixture.Service.CancelAsync(reservation.Id));

        Assert.Equal(BorrowingErrors.ReservationNotFound, exception.Error);
        Assert.Equal(ReservationStatus.Waiting, reservation.Status);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
        Assert.Empty(fixture.Lock.Resources);
    }

    [Fact]
    public async Task CancelAsync_transfers_a_held_copy_to_the_next_waiter_without_changing_copy_status()
    {
        var fixture = new Fixture();
        Guid copyId = Guid.NewGuid();
        Reservation ready = fixture.AddReady(copyId);
        Reservation next = fixture.AddWaiting(1);
        Reservation afterNext = fixture.AddWaiting(2);
        fixture.Catalog.Copies[copyId] = Copy(copyId, BookId) with { Status = BookCopyStatus.Reserved };

        ReservationDto result = await fixture.Service.CancelAsync(ready.Id);

        Assert.Equal(ReservationStatus.Cancelled, result.Status);
        Assert.Equal(ReservationStatus.ReadyForPickup, next.Status);
        Assert.Equal(copyId, next.CopyId);
        Assert.NotNull(next.ReadyUntilUtc);
        Assert.Equal(ReservationStatus.Waiting, afterNext.Status);
        Assert.Equal(1, afterNext.QueuePosition);
        Assert.Equal(BookCopyStatus.Reserved, fixture.Catalog.Copies[copyId].Status);
        Assert.Equal(0, fixture.Catalog.SetStatusCalls);
        Assert.Single(fixture.Events.Published.OfType<ReservationReady>());
    }

    [Fact]
    public async Task CancelAsync_releases_a_held_copy_when_no_one_is_waiting()
    {
        var fixture = new Fixture();
        Guid copyId = Guid.NewGuid();
        Reservation ready = fixture.AddReady(copyId);
        fixture.Catalog.Copies[copyId] = Copy(copyId, BookId) with { Status = BookCopyStatus.Reserved };

        await fixture.Service.CancelAsync(ready.Id);

        Assert.Equal(BookCopyStatus.Available, fixture.Catalog.Copies[copyId].Status);
        Assert.Equal(1, fixture.Catalog.SetStatusCalls);
        Assert.Empty(fixture.Events.Published.OfType<ReservationReady>());
    }

    [Fact]
    public async Task CancelAsync_rejects_a_second_cancellation()
    {
        var fixture = new Fixture();
        Reservation reservation = fixture.AddWaiting(1);

        await fixture.Service.CancelAsync(reservation.Id);
        DomainException exception = await Assert.ThrowsAsync<DomainException>(
            () => fixture.Service.CancelAsync(reservation.Id));

        Assert.Equal(BorrowingErrors.ReservationCannotBeCancelled, exception.Error);
    }

    private static async Task AssertRejected(Fixture fixture, Error expectedError)
    {
        DomainException exception = await Assert.ThrowsAsync<DomainException>(
            () => fixture.Service.CreateAsync(new CreateReservationRequest(BookId, StaffId)));

        Assert.Equal(expectedError, exception.Error);
        Assert.Equal(0, fixture.UnitOfWork.CommitCount);
        Assert.Equal(1, fixture.UnitOfWork.RollbackCount);
        Assert.Empty(fixture.Reservations.Saved);
    }

    private static BookDto Book(bool isDeleted = false) => new(
        BookId, "Test book", "9780000000000", 2020, "English", null,
        Guid.NewGuid(), "Category", null, null, Array.Empty<string>(), 1, 0, isDeleted);

    private static BookCopyDto Copy(Guid copyId, Guid bookId) => new(
        copyId, bookId, "BK-TEST", BookCopyStatus.Available, BookCopyCondition.Good, "A-1");

    private sealed class Fixture
    {
        public readonly ReservationRepositoryFake Reservations = new();
        public readonly LoansFake Loans = new();
        public readonly CatalogFake Catalog = new();
        public readonly StaffFake Staff = new();
        public readonly LockFake Lock = new();
        public readonly FakeEventBus Events = new();
        public readonly UnitOfWorkFake UnitOfWork;
        public readonly FakeIdGenerator Ids = new();
        public readonly FakeCurrentUser CurrentUser = new();   // a Librarian unless a test says otherwise
        public StaffMemberStatus Status
        {
            set => Staff.Status = value;
        }
        public int ActiveCount
        {
            set => Reservations.ActiveCount = value;
        }
        public int WaitingCount
        {
            set => Reservations.WaitingCount = value;
        }
        public bool HasActiveForBook
        {
            set => Reservations.HasActiveForBook = value;
        }
        public ReservationService Service { get; }

        public Fixture(Action<BorrowingOptions>? settings = null)
        {
            Catalog.Book = Book();
            UnitOfWork = new UnitOfWorkFake(Reservations, Events);
            var resolvedSettings = BorrowingTestSettings.Create(settings);
            var clock = new TestClock(Now);
            var queue = new ReservationQueueService(
                Reservations, Catalog, Lock, Events, new WeekdayCalendar(), clock, Ids, resolvedSettings);
            Service = new ReservationService(
                Staff, Catalog, Loans, Reservations, queue, UnitOfWork, Lock, CurrentUser,
                clock, Ids, resolvedSettings);
        }

        public Reservation AddWaiting(int position)
        {
            Reservation reservation = Reservation.Create(
                Guid.NewGuid(), BookId, Guid.NewGuid(), Now.AddMinutes(position), position);
            Reservations.Saved.Add(reservation);
            return reservation;
        }

        public Reservation AddReady(Guid copyId)
        {
            Reservation reservation = Reservation.Create(
                Guid.NewGuid(), BookId, Guid.NewGuid(), Now.AddMinutes(-10), 1);
            reservation.MarkReady(copyId, Now.AddDays(3));
            Reservations.Saved.Add(reservation);
            return reservation;
        }
    }

    private sealed class UnitOfWorkFake(ReservationRepositoryFake reservations, FakeEventBus events) : IUnitOfWork
    {
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }

        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            try
            {
                T result = await operation(ct);
                reservations.Commit();
                events.Commit();
                CommitCount++;
                return result;
            }
            catch
            {
                reservations.Rollback();
                events.Rollback();
                RollbackCount++;
                throw;
            }
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
            ExecuteInTransactionAsync<object?>(async token => { await operation(token); return null; }, ct);
    }

    private sealed class LockFake : IApplicationLock
    {
        public List<string> Resources { get; } = new();
        public Task AcquireAsync(string resource, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Resources.Add(resource);
            return Task.CompletedTask;
        }
    }

    private sealed class StaffFake : IStaffMemberService
    {
        public StaffMemberStatus Status { get; set; } = StaffMemberStatus.Active;
        public Task<EligibilityResult> CanBorrowAsync(Guid staffMemberId, CancellationToken ct = default) =>
            Task.FromResult(Status switch
            {
                StaffMemberStatus.Active => EligibilityResult.Allowed(),
                StaffMemberStatus.Restricted => EligibilityResult.Allowed(),
                StaffMemberStatus.Suspended => EligibilityResult.Denied(StaffErrors.Suspended),
                StaffMemberStatus.Deactivated => EligibilityResult.Denied(StaffErrors.Deactivated),
                _ => throw new ArgumentOutOfRangeException()
            });
        public Task<StaffMemberStatus> GetStatusAsync(Guid staffMemberId, CancellationToken ct = default) => Task.FromResult(Status);
        public Task<LendingRules> GetLendingRulesAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetStatusAsync(Guid staffMemberId, StaffMemberStatus status, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> staffMemberIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffMemberLookup?> FindAsync(string employeeIdOrEmail, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class CatalogFake : ICatalogService
    {
        public BookDto? Book { get; set; }
        public BookCopyDto? AvailableCopy { get; set; }
        public Dictionary<Guid, BookCopyDto> Copies { get; } = new();
        public IReadOnlyList<Guid> LastResolvedCopyIds { get; private set; } = Array.Empty<Guid>();
        public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default) => Task.FromResult(Book);
        public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default) => Task.FromResult(AvailableCopy);
        public Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default)
        {
            LastResolvedCopyIds = copyIds.ToArray();
            IReadOnlyList<BookCopyDto> result = copyIds.Where(Copies.ContainsKey).Select(id => Copies[id]).ToList();
            return Task.FromResult(result);
        }
        public int SetStatusCalls { get; private set; }
        public Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default)
        {
            SetStatusCalls++;
            Copies[copyId] = Copies[copyId] with { Status = status };
            return Task.CompletedTask;
        }
        public Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class LoansFake : ILoanRepository
    {
        public Task<Loan?> GetForUpdateAsync(
            Guid loanId,
            CancellationToken ct = default)
        {
            throw new NotSupportedException(
                "Reservation creation should not load a loan for modification.");
        }
        public List<Guid> CopyIds { get; } = new();
        public Task<IReadOnlyList<Guid>> GetOpenLoanCopyIdsAsync(Guid staffMemberId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>(CopyIds);
        public Task<int> CountOpenLoansAsync(Guid staffMemberId, CancellationToken ct = default) => Task.FromResult(0);
        public Task AddAsync(Loan loan, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Loan?> FindAsync(Guid loanId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Loan?> FindOpenByCopyAsync(Guid copyId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ReservationRepositoryFake : IReservationRepository
    {
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        private readonly List<Reservation> _pending = new();
        public List<Reservation> Saved { get; } = new();
        public int ActiveCount { get; set; }
        public int WaitingCount { get; set; }
        public bool HasActiveForBook { get; set; }
        public Task<int> CountActiveAsync(Guid staffMemberId, CancellationToken ct = default) => Task.FromResult(ActiveCount);
        public Task<bool> HasActiveForBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) => Task.FromResult(HasActiveForBook);
        public Task<int> CountWaitingForBookAsync(Guid bookId, CancellationToken ct = default) => Task.FromResult(WaitingCount);
        public Task<IReadOnlyList<Reservation>> GetWaitingForBookAsync(Guid bookId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Reservation>>(Saved
                .Where(r => r.BookId == bookId && r.Status == ReservationStatus.Waiting)
                .OrderBy(r => r.QueuePosition)
                .ToList());
        public Task<Reservation?> FindReadyForCopyAsync(Guid copyId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Reservation?> FindActiveForStaffAndBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Reservation reservation, CancellationToken ct = default)
        {
            _pending.Add(reservation);
            return Task.CompletedTask;
        }
        public Task<Reservation?> FindAsync(Guid reservationId, CancellationToken ct = default) =>
            Task.FromResult(Saved.SingleOrDefault(r => r.Id == reservationId));
        public Task<Reservation?> GetForUpdateAsync(Guid reservationId, CancellationToken ct = default) => FindAsync(reservationId, ct);
        public void Commit()
        {
            Saved.AddRange(_pending);
            _pending.Clear();
        }
        public void Rollback() => _pending.Clear();
    }

    private sealed class WeekdayCalendar : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        public DateOnly AddWorkingDays(DateOnly from, int days)
        {
            DateOnly date = from;
            while (days > 0)
            {
                date = date.AddDays(1);
                if (IsWorkingDay(date)) days--;
            }
            return date;
        }

        public int CountWorkingDays(DateOnly fromExclusive, DateOnly toInclusive) => throw new NotSupportedException();
    }
}
