using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
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
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Borrowing;

public sealed class LoanServiceTests
{
    private const string Barcode = "BK-001";

    private readonly Guid _staffId = Guid.NewGuid();
    private readonly Guid _copyId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeStaffMemberService _staff = new();
    private readonly FakeLoanRepository _loans = new();
    private readonly ReservationRepositoryFake _reservations = new();
    private readonly InMemoryCatalogService _catalog = new();
    private readonly FakeIdGenerator _ids = new();
    private readonly FakeEventBus _events = new();
    private readonly FakeApplicationLock _locks;
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly BorrowingOptions _settings = BorrowingTestSettings.Defaults();
    private readonly LoanService _service;

    public LoanServiceTests()
    {
        // The lock writes into the staff call log, so a test can see what happened first.
        _locks = new FakeApplicationLock(_staff.Calls);
        _unitOfWork = new FakeUnitOfWork(_loans, _catalog, _events);
        var clock = new TestClock(_now);
        var queue = new ReservationQueueService(_reservations, _catalog, _locks, _events, new TestCalendar(), clock, _ids,
            BorrowingTestSettings.Create());
        _service = new LoanService(_staff, _catalog, _loans, _reservations, queue, _unitOfWork, _locks, _events, clock, _ids,
            Options.Create(_settings));
    }

    [Fact]
    public async Task IssueAsync_creates_loan_and_marks_copy_on_loan()
    {
        SeedCopy(BookCopyStatus.Available);

        LoanDto result = await _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode));

        Assert.Equal(_ids.Id, result.LoanId);
        Assert.Equal(_staffId, result.StaffMemberId);
        Assert.Equal(_copyId, result.BookCopyId);
        Assert.Equal(_now, result.IssuedAtUtc);
        Assert.Equal(_now.AddDays(_staff.Rules.LoanDays), result.DueAtUtc);
        Assert.Equal(LoanStatus.Active, result.Status);
        Assert.Equal(0, result.RenewalCount);

        Loan saved = Assert.Single(_loans.Saved);
        Assert.Equal(result.LoanId, saved.Id);
        Assert.Equal(result.DueAtUtc, saved.DueAtUtc);
        Assert.Empty(_loans.Pending);

        Assert.Equal(BookCopyStatus.OnLoan, (await _catalog.GetCopyByBarcodeAsync(Barcode))!.Status);
        Assert.Equal(1, _unitOfWork.CommittedCount);
        Assert.Equal(0, _unitOfWork.RolledBackCount);
    }

    [Fact]
    public async Task IssueAsync_publishes_LoanIssued_with_the_loan_and_the_book()
    {
        var bookId = Guid.NewGuid();
        _catalog.Seed(new BookCopyDto(_copyId, bookId, Barcode, BookCopyStatus.Available, BookCopyCondition.Good, "A-1"));

        LoanDto loan = await _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode));

        var issued = Assert.IsType<LoanIssued>(Assert.Single(_events.Published));
        Assert.Equal(loan.LoanId, issued.LoanId);
        Assert.Equal(_staffId, issued.StaffMemberId);
        Assert.Equal(_copyId, issued.BookCopyId);
        Assert.Equal(bookId, issued.BookId);
        Assert.Equal(loan.DueAtUtc, issued.DueAtUtc);
        Assert.Equal(_now, issued.OccurredAtUtc);
    }

    [Fact]
    public async Task GetAsync_returns_the_loan_and_refuses_an_unknown_id()
    {
        SeedCopy(BookCopyStatus.Available);
        LoanDto issued = await _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode));

        Assert.Equal(issued, await _service.GetAsync(issued.LoanId));
        var error = await Assert.ThrowsAsync<DomainException>(() => _service.GetAsync(Guid.NewGuid()));
        Assert.Equal(BorrowingErrors.LoanNotFound, error.Error);
    }

    [Fact]
    public async Task IssueAsync_takes_the_shared_staff_lock_before_any_check()
    {
        SeedCopy(BookCopyStatus.Available);

        await _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode));

        // The same resource Staff takes for deactivation (BR-MEM-4).
        Assert.Equal($"lock:{BorrowingLocks.Staff(_staffId)}", _staff.Calls[0]);
        Assert.Equal(nameof(FakeStaffMemberService.CanBorrowAsync), _staff.Calls[1]);
        string[] lockCalls = _locks.Log.Where(entry => entry.StartsWith("lock:", StringComparison.Ordinal)).ToArray();
        Assert.Equal([$"lock:{BorrowingLocks.Staff(_staffId)}", $"lock:{BorrowingLocks.Book(_catalog.Snapshot()[_copyId].BookId)}"], lockCalls);
    }

    [Fact]
    public async Task IssueAsync_rejects_suspended_staff_before_other_checks()
    {
        SeedCopy(BookCopyStatus.Available);
        _staff.Eligibility = Abstractions.Staff.EligibilityResult.Denied(StaffErrors.Suspended);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode)));

        Assert.Equal(StaffErrors.Suspended.Code, error.Error.Code);
        Assert.Equal(0, _loans.CountCalls);
        Assert.Equal(0, _catalog.LookupCalls);
        AssertNothingIssued(BookCopyStatus.Available);
    }

    [Fact]
    public async Task IssueAsync_rejects_when_the_loan_limit_is_reached()
    {
        SeedCopy(BookCopyStatus.Available);
        for (var i = 0; i < _staff.Rules.MaxLoans; i++)
            _loans.Seed(Loan.Issue(Guid.NewGuid(), Guid.NewGuid(), _staffId, _now.AddDays(-1), 14));

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode)));

        // Agreed with Group 2: Staff's error and text, "Loan limit reached (3/3)."
        Assert.Equal("staff.loan_limit_reached", error.Error.Code);
        Assert.Equal("Loan limit reached (3/3).", error.Error.Message);
        Assert.Equal(0, _catalog.LookupCalls);
        Assert.Equal(_staff.Rules.MaxLoans, _loans.Saved.Count);
        Assert.Equal(BookCopyStatus.Available, (await _catalog.GetCopyByBarcodeAsync(Barcode))!.Status);
    }

    [Fact]
    public async Task IssueAsync_rejects_an_unknown_barcode()
    {
        SeedCopy(BookCopyStatus.Available);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            _service.IssueAsync(new IssueLoanRequest(_staffId, "BK-404")));

        Assert.Equal(BorrowingErrors.CopyNotFound.Code, error.Error.Code);
        AssertNothingIssued(BookCopyStatus.Available);
    }

    [Theory]
    [InlineData(BookCopyStatus.OnLoan)]
    [InlineData(BookCopyStatus.Reserved)]
    [InlineData(BookCopyStatus.UnderRepair)]
    [InlineData(BookCopyStatus.Lost)]
    [InlineData(BookCopyStatus.Withdrawn)]
    public async Task IssueAsync_rejects_a_copy_that_is_not_available(BookCopyStatus status)
    {
        SeedCopy(status);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode)));

        Assert.Equal(BorrowingErrors.CopyNotAvailable.Code, error.Error.Code);
        AssertNothingIssued(status);
    }

    [Fact]
    public async Task IssueAsync_rolls_back_the_loan_when_catalog_fails()
    {
        SeedCopy(BookCopyStatus.Available);
        _catalog.ThrowOnNextSetCopyStatus = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode)));

        AssertNothingIssued(BookCopyStatus.Available);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task IssueAsync_fulfils_the_reservation_when_its_held_copy_is_collected()
    {
        Guid bookId = Guid.NewGuid();
        SeedCopy(BookCopyStatus.Reserved, bookId);
        Reservation held = Ready(bookId, _staffId, _copyId);
        _reservations.Seed(held);

        LoanDto issued = await _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode));

        Assert.Equal(LoanStatus.Active, issued.Status);
        Assert.Equal(ReservationStatus.Fulfilled, held.Status);
        Assert.Equal(BookCopyStatus.OnLoan, (await _catalog.GetCopyByBarcodeAsync(Barcode))!.Status);
    }

    [Fact]
    public async Task IssueAsync_rejects_a_held_copy_for_anyone_except_its_reservation_owner()
    {
        Guid bookId = Guid.NewGuid();
        SeedCopy(BookCopyStatus.Reserved, bookId);
        Reservation held = Ready(bookId, Guid.NewGuid(), _copyId);
        _reservations.Seed(held);

        DomainException error = await Assert.ThrowsAsync<DomainException>(() =>
            _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode)));

        Assert.Equal(BorrowingErrors.CopyNotAvailable.Code, error.Error.Code);
        Assert.Equal(ReservationStatus.ReadyForPickup, held.Status);
        AssertNothingIssued(BookCopyStatus.Reserved);
    }

    [Fact]
    public async Task IssueAsync_fulfils_ready_reservation_and_offers_its_held_copy_to_the_next_member()
    {
        Guid bookId = Guid.NewGuid();
        SeedCopy(BookCopyStatus.Available, bookId);
        Guid heldCopyId = Guid.NewGuid();
        string heldBarcode = "BK-HELD";
        _catalog.Seed(new BookCopyDto(heldCopyId, bookId, heldBarcode, BookCopyStatus.Reserved, BookCopyCondition.Good, "A-2"));
        Reservation fulfilled = Ready(bookId, _staffId, heldCopyId);
        Reservation next = _reservations.Wait(bookId, _now.AddDays(-1), 1);
        _reservations.Seed(fulfilled);

        await _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode));

        Assert.Equal(ReservationStatus.Fulfilled, fulfilled.Status);
        Assert.Equal((ReservationStatus.ReadyForPickup, heldCopyId), (next.Status, next.CopyId));
        Assert.Equal(1, _reservations.Flushes);   // the fulfilled reservation is written before the copy is held again
        Assert.Equal(BookCopyStatus.Reserved, (await _catalog.GetCopyByBarcodeAsync(heldBarcode))!.Status);
        Assert.Equal(BookCopyStatus.OnLoan, (await _catalog.GetCopyByBarcodeAsync(Barcode))!.Status);
    }

    [Fact]
    public async Task IssueAsync_releases_the_freed_held_copy_when_nobody_else_is_waiting()
    {
        Guid bookId = Guid.NewGuid();
        SeedCopy(BookCopyStatus.Available, bookId);
        Guid heldCopyId = Guid.NewGuid();
        const string heldBarcode = "BK-HELD";
        _catalog.Seed(new BookCopyDto(heldCopyId, bookId, heldBarcode, BookCopyStatus.Reserved, BookCopyCondition.Good, "A-2"));
        Reservation held = Ready(bookId, _staffId, heldCopyId);
        _reservations.Seed(held);

        await _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode));

        Assert.Equal(ReservationStatus.Fulfilled, held.Status);
        Assert.Equal(BookCopyStatus.Available, (await _catalog.GetCopyByBarcodeAsync(heldBarcode))!.Status);
    }

    [Fact]
    public async Task IssueAsync_cancels_the_members_waiting_reservation_for_the_book_and_moves_the_queue_up()
    {
        Guid bookId = Guid.NewGuid();
        SeedCopy(BookCopyStatus.Available, bookId);
        Reservation own = Reservation.Create(Guid.NewGuid(), bookId, _staffId, _now.AddDays(-2), 1);
        _reservations.Seed(own);
        Reservation behind = _reservations.Wait(bookId, _now.AddDays(-1), 2);

        await _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode));

        Assert.Equal(ReservationStatus.Cancelled, own.Status);
        Assert.Equal((ReservationStatus.Waiting, 1), (behind.Status, behind.QueuePosition));
    }

    [Fact]
    public async Task IssueAsync_keeps_a_ready_reservation_and_its_held_copy_when_any_copy_does_not_fulfil()
    {
        _settings.Reservations.AnyCopyFulfils = false;
        Guid bookId = Guid.NewGuid();
        SeedCopy(BookCopyStatus.Available, bookId);
        Guid heldCopyId = Guid.NewGuid();
        const string heldBarcode = "BK-HELD";
        _catalog.Seed(new BookCopyDto(heldCopyId, bookId, heldBarcode, BookCopyStatus.Reserved, BookCopyCondition.Good, "A-2"));
        Reservation held = Ready(bookId, _staffId, heldCopyId);
        _reservations.Seed(held);

        await _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode));

        Assert.Equal(ReservationStatus.ReadyForPickup, held.Status);
        Assert.Equal(BookCopyStatus.Reserved, (await _catalog.GetCopyByBarcodeAsync(heldBarcode))!.Status);
        Assert.Equal(BookCopyStatus.OnLoan, (await _catalog.GetCopyByBarcodeAsync(Barcode))!.Status);
    }

    [Fact]
    public async Task IssueAsync_keeps_a_waiting_reservation_when_any_copy_does_not_fulfil()
    {
        _settings.Reservations.AnyCopyFulfils = false;
        Guid bookId = Guid.NewGuid();
        SeedCopy(BookCopyStatus.Available, bookId);
        Reservation own = Reservation.Create(Guid.NewGuid(), bookId, _staffId, _now.AddDays(-2), 1);
        _reservations.Seed(own);

        await _service.IssueAsync(new IssueLoanRequest(_staffId, Barcode));

        Assert.Equal((ReservationStatus.Waiting, 1), (own.Status, own.QueuePosition));
    }

    private void SeedCopy(BookCopyStatus status, Guid? bookId = null) =>
        _catalog.Seed(new BookCopyDto(_copyId, bookId ?? Guid.NewGuid(), Barcode, status, BookCopyCondition.Good, "A-1"));

    private Reservation Ready(Guid bookId, Guid staffMemberId, Guid copyId)
    {
        Reservation reservation = Reservation.Create(Guid.NewGuid(), bookId, staffMemberId, _now.AddDays(-2), 1);
        reservation.MarkReady(copyId, _now.AddDays(2));
        return reservation;
    }

    private void AssertNothingIssued(BookCopyStatus copyStatus)
    {
        Assert.Empty(_loans.Saved);
        Assert.Empty(_loans.Pending);
        Assert.Equal(0, _unitOfWork.CommittedCount);
        Assert.Equal(1, _unitOfWork.RolledBackCount);
        Assert.Equal(copyStatus, _catalog.Snapshot()[_copyId].Status);
    }

    private sealed class ReservationRepositoryFake : IReservationRepository
    {
        private readonly List<Reservation> _items = new();
        public int Flushes { get; private set; }
        public Task FlushAsync(CancellationToken ct = default)
        {
            Flushes++;
            return Task.CompletedTask;
        }
        public void Seed(Reservation item) => _items.Add(item);
        public Reservation Wait(Guid bookId, DateTime createdAtUtc, int position)
        {
            Reservation item = Reservation.Create(Guid.NewGuid(), bookId, Guid.NewGuid(), createdAtUtc, position);
            _items.Add(item);
            return item;
        }
        public Task<IReadOnlyList<Reservation>> GetWaitingForBookAsync(Guid bookId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Reservation>>(_items.Where(r => r.BookId == bookId && r.Status == ReservationStatus.Waiting)
                .OrderBy(r => r.QueuePosition).ThenBy(r => r.CreatedAtUtc).ToArray());
        public Task<Reservation?> FindReadyForCopyAsync(Guid copyId, CancellationToken ct = default) =>
            Task.FromResult(_items.SingleOrDefault(r => r.CopyId == copyId && r.Status == ReservationStatus.ReadyForPickup));
        public Task<Reservation?> FindActiveForStaffAndBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) =>
            Task.FromResult(_items.SingleOrDefault(r => r.StaffMemberId == staffMemberId && r.BookId == bookId
                && r.Status is ReservationStatus.Waiting or ReservationStatus.ReadyForPickup));
        public Task<int> CountActiveAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Reservation>> FindActiveForStaffAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasActiveForBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountWaitingForBookAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Reservation reservation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Reservation?> FindAsync(Guid reservationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Reservation?> GetForUpdateAsync(Guid reservationId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class TestCalendar : IWorkingDayCalendar
    {
        public bool IsWorkingDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        public DateOnly AddWorkingDays(DateOnly from, int days)
        {
            while (days > 0)
            {
                from = from.AddDays(1);
                if (IsWorkingDay(from)) days--;
            }
            return from;
        }
        public int CountWorkingDays(DateOnly fromExclusive, DateOnly toInclusive) => throw new NotSupportedException();
    }
}
