using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Borrowing;

public sealed class LoanServiceTests
{
    private const string Barcode = "BK-001";

    private readonly Guid _staffId = Guid.NewGuid();
    private readonly Guid _copyId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeStaffMemberService _staff = new();
    private readonly FakeLoanRepository _loans = new();
    private readonly InMemoryCatalogService _catalog = new();
    private readonly FakeIdGenerator _ids = new();
    private readonly FakeEventBus _events = new();
    private readonly FakeApplicationLock _locks;
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly LoanService _service;

    public LoanServiceTests()
    {
        // The lock writes into the staff call log, so a test can see what happened first.
        _locks = new FakeApplicationLock(_staff.Calls);
        _unitOfWork = new FakeUnitOfWork(_loans, _catalog, _events);
        _service = new LoanService(_staff, _catalog, _loans, _unitOfWork, _locks, _events, new TestClock(_now), _ids);
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
        Assert.Single(_locks.Log, entry => entry.StartsWith("lock:", StringComparison.Ordinal));
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

    private void SeedCopy(BookCopyStatus status) =>
        _catalog.Seed(new BookCopyDto(_copyId, Guid.NewGuid(), Barcode, status, BookCopyCondition.Good, "A-1"));

    private void AssertNothingIssued(BookCopyStatus copyStatus)
    {
        Assert.Empty(_loans.Saved);
        Assert.Empty(_loans.Pending);
        Assert.Equal(0, _unitOfWork.CommittedCount);
        Assert.Equal(1, _unitOfWork.RolledBackCount);
        Assert.Equal(copyStatus, _catalog.Snapshot()[_copyId].Status);
    }
}
