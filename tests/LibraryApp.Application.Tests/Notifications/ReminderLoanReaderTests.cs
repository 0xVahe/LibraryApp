using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Notifications.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Notifications;

/// <summary>NOT-05: the reminder loans come from the contracts of Borrowing, Catalog and Staff, not their tables.</summary>
public sealed class ReminderLoanReaderTests
{
    private static readonly DateOnly Wednesday21 = new(2026, 10, 21);
    private static readonly DateOnly Thursday22 = new(2026, 10, 22);

    private static readonly Guid Anna = Guid.NewGuid();
    private static readonly Guid Hayk = Guid.NewGuid();   // left the bank
    private static readonly Guid CleanCode = Guid.NewGuid();
    private static readonly Guid EffectiveJava = Guid.NewGuid();

    private readonly BorrowingFake _borrowing = new();
    private readonly CatalogFake _catalog = new();
    private readonly StaffFake _staff = new();

    public ReminderLoanReaderTests()
    {
        _staff.Contacts[Anna] = new StaffContact(
            Anna, "Anna Petrosyan", "anna.petrosyan@ameriabank.am", "manager@ameriabank.am", StaffMemberStatus.Active);
        _staff.Contacts[Hayk] = new StaffContact(
            Hayk, "Hayk Manukyan", "hayk.manukyan@ameriabank.am", null, StaffMemberStatus.Deactivated);
        _catalog.Titles[CleanCode] = "Clean Code";
        _catalog.Titles[EffectiveJava] = "Effective Java";
    }

    private ReminderLoanReader CreateReader() => new(_borrowing, _catalog, _staff);

    [Fact]
    public async Task A_due_loan_gets_the_recipient_from_Staff_and_the_title_from_Catalog()
    {
        OpenLoanInfo loan = _borrowing.Due(Wednesday21, Anna, CleanCode);

        var loans = await CreateReader().GetLoansDueOnAsync([Wednesday21]);

        Assert.Equal(
            [new ReminderLoan(loan.LoanId, Anna, "Anna Petrosyan", "anna.petrosyan@ameriabank.am", "manager@ameriabank.am", "Clean Code", loan.DueAtUtc)],
            loans);
    }

    [Fact]
    public async Task Loans_of_every_date_earliest_due_first()
    {
        OpenLoanInfo thursday = _borrowing.Due(Thursday22, Anna, EffectiveJava);
        OpenLoanInfo wednesday = _borrowing.Due(Wednesday21, Anna, CleanCode);
        _borrowing.Due(new DateOnly(2026, 10, 23), Anna, CleanCode);   // not asked for

        var loans = await CreateReader().GetLoansDueOnAsync([Thursday22, Wednesday21]);

        Assert.Equal([wednesday.LoanId, thursday.LoanId], loans.Select(loan => loan.LoanId));
        Assert.Equal([Thursday22, Wednesday21], _borrowing.DatesAsked);   // only the dates asked for, one call each
    }

    [Fact]
    public async Task Overdue_loans_are_the_ones_Borrowing_returns()
    {
        OpenLoanInfo overdue = _borrowing.Overdue(new DateOnly(2026, 10, 12), Anna, CleanCode);

        var loans = await CreateReader().GetOverdueLoansAsync();

        ReminderLoan loan = Assert.Single(loans);
        Assert.Equal(overdue.LoanId, loan.LoanId);
        Assert.Equal("Clean Code", loan.BookTitle);
    }

    [Fact]
    public async Task Loans_of_staff_who_left_the_bank_are_left_out()
    {
        _borrowing.Due(Wednesday21, Hayk, CleanCode);
        _borrowing.Overdue(new DateOnly(2026, 10, 12), Hayk, EffectiveJava);

        Assert.Empty(await CreateReader().GetLoansDueOnAsync([Wednesday21]));
        Assert.Empty(await CreateReader().GetOverdueLoansAsync());
    }

    [Fact]
    public async Task A_loan_of_an_unknown_staff_member_or_book_is_left_out()
    {
        _borrowing.Due(Wednesday21, Guid.NewGuid(), CleanCode);
        _borrowing.Due(Wednesday21, Anna, Guid.NewGuid());

        Assert.Empty(await CreateReader().GetLoansDueOnAsync([Wednesday21]));
    }

    [Fact]
    public async Task One_call_to_Staff_for_all_members_and_one_call_per_book_not_per_loan_or_member()
    {
        var elen = Guid.NewGuid();
        _staff.Contacts[elen] = new StaffContact(elen, "Elen Danielyan", "elen.danielyan@ameriabank.am", null, StaffMemberStatus.Active);
        _borrowing.Due(Wednesday21, Anna, CleanCode);
        _borrowing.Due(Wednesday21, Anna, CleanCode);
        _borrowing.Due(Wednesday21, Anna, EffectiveJava);
        _borrowing.Due(Wednesday21, elen, CleanCode);
        _borrowing.Due(Wednesday21, Hayk, CleanCode);

        var loans = await CreateReader().GetLoansDueOnAsync([Wednesday21]);

        Assert.Equal(4, loans.Count);                                   // Hayk left the bank
        var request = Assert.Single(_staff.Requests);                   // all members in one call, none twice
        Assert.Equal(new[] { Anna, elen, Hayk }.Order(), request.Order());
        Assert.Equal(2, _catalog.Calls);
    }

    [Fact]
    public async Task The_status_comes_with_the_contact_so_Staff_is_not_asked_for_it_member_by_member()
    {
        _borrowing.Due(Wednesday21, Anna, CleanCode);
        _borrowing.Due(Wednesday21, Hayk, CleanCode);

        await CreateReader().GetLoansDueOnAsync([Wednesday21]);

        Assert.Equal(0, _staff.StatusCalls);
    }

    [Fact]
    public async Task Overdue_and_due_loans_each_make_one_call_to_Staff()
    {
        _borrowing.Due(Wednesday21, Anna, CleanCode);
        _borrowing.Overdue(new DateOnly(2026, 10, 12), Anna, CleanCode);

        await CreateReader().GetLoansDueOnAsync([Wednesday21]);
        await CreateReader().GetOverdueLoansAsync();

        Assert.Equal(2, _staff.Requests.Count);
    }

    private sealed class BorrowingFake : IBorrowingReadService
    {
        private readonly List<OpenLoanInfo> _loans = new();

        public List<DateOnly> DatesAsked { get; } = new();

        public OpenLoanInfo Due(DateOnly date, Guid staffMemberId, Guid bookId) =>
            Add(date, staffMemberId, bookId, LoanStatus.Active);

        public OpenLoanInfo Overdue(DateOnly date, Guid staffMemberId, Guid bookId) =>
            Add(date, staffMemberId, bookId, LoanStatus.Overdue);

        private OpenLoanInfo Add(DateOnly date, Guid staffMemberId, Guid bookId, LoanStatus status)
        {
            DateTime dueAtUtc = date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);
            var loan = new OpenLoanInfo(
                Guid.NewGuid(), staffMemberId, Guid.NewGuid(), bookId, dueAtUtc, status, dueAtUtc.AddDays(-21), 0);
            _loans.Add(loan);
            return loan;
        }

        public Task<IReadOnlyList<OpenLoanInfo>> GetLoansDueOnAsync(DateOnly localDate, CancellationToken ct = default)
        {
            DatesAsked.Add(localDate);
            return Task.FromResult<IReadOnlyList<OpenLoanInfo>>(_loans
                .Where(loan => DateOnly.FromDateTime(loan.DueAtUtc) == localDate)
                .OrderBy(loan => loan.DueAtUtc)
                .ToList());
        }

        public Task<IReadOnlyList<OpenLoanInfo>> GetOverdueLoansAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OpenLoanInfo>>(_loans
                .Where(loan => loan.Status == LoanStatus.Overdue)
                .OrderBy(loan => loan.DueAtUtc)
                .ToList());

        public Task<IReadOnlyList<OpenLoanInfo>> GetActiveLoansAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<StaffBorrowingSummary> GetStaffSummaryAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class CatalogFake : ICatalogService
    {
        public Dictionary<Guid, string> Titles { get; } = new();
        public int Calls { get; private set; }

        public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Titles.TryGetValue(bookId, out string? title)
                ? new BookDto(bookId, title, "9780132350884", 2008, "en", null, Guid.NewGuid(), "Technology", null, null,
                    ["Author"], TotalCopies: 1, AvailableCopies: 0, IsDeleted: false)
                : null);
        }

        public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class StaffFake : IStaffMemberService
    {
        public Dictionary<Guid, StaffContact> Contacts { get; } = new();
        public List<Guid[]> Requests { get; } = [];
        public int StatusCalls { get; private set; }

        public Task<IReadOnlyDictionary<Guid, StaffContact>> GetContactsAsync(
            IReadOnlyCollection<Guid> staffMemberIds, CancellationToken ct = default)
        {
            Requests.Add(staffMemberIds.ToArray());
            return Task.FromResult<IReadOnlyDictionary<Guid, StaffContact>>(
                staffMemberIds.Where(Contacts.ContainsKey).ToDictionary(id => id, id => Contacts[id]));
        }

        public Task<StaffMemberStatus> GetStatusAsync(Guid staffMemberId, CancellationToken ct = default)
        {
            StatusCalls++;
            throw new NotSupportedException();
        }

        public Task<EligibilityResult> CanBorrowAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LendingRules> GetLendingRulesAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetStatusAsync(Guid staffMemberId, StaffMemberStatus status, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            IReadOnlyCollection<Guid> staffMemberIds, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<StaffMemberLookup?> FindAsync(string employeeIdOrEmail, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
