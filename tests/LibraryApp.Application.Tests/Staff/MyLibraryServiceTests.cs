using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Application.Tests.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryApp.Application.Tests.Staff;

public class MyLibraryServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid BookA = Guid.NewGuid();
    private static readonly Guid BookB = Guid.NewGuid();
    private static readonly Guid CopyA = Guid.NewGuid();
    private static readonly Guid CopyB = Guid.NewGuid();

    private readonly FakeCurrentUser _user = new() { Role = StaffRoles.StaffMember };
    private readonly Members _members = new();
    private readonly Borrowing _borrowing = new();
    private readonly Catalog _catalog = new();

    private MyLibraryService Service => new(_user, _members, _borrowing, _catalog, new TestClock(Now),
        NullLogger<MyLibraryService>.Instance);

    public MyLibraryServiceTests()
    {
        _members.Add(_user.StaffMemberId!.Value);
        _catalog.Books[BookA] = "Risk Management Basics";
        _catalog.Books[BookB] = "Clean Code in C#";
        _catalog.Copies[CopyA] = (BookA, "BK-0001-01");
        _catalog.Copies[CopyB] = (BookB, "BK-0002-01");
    }

    private static OpenLoanInfo Loan(Guid member, Guid copy, Guid book, DateTime due, LoanStatus status = LoanStatus.Active) =>
        new(Guid.NewGuid(), member, copy, book, due, status, due.AddDays(-21), 0);

    [Fact]
    public async Task Returns_profile_loans_reservations_and_penalties_with_titles_and_barcodes()
    {
        var me = _user.StaffMemberId!.Value;
        var loan = Loan(me, CopyA, BookA, Now.AddDays(5));
        var reservation = new OpenReservationInfo(Guid.NewGuid(), BookB, ReservationStatus.ReadyForPickup, 0,
            Now.AddDays(2), Now.AddDays(-1));
        var penalty = new ActivePenaltyInfo(Guid.NewGuid(), Guid.NewGuid(), BookB, 2, "Returned late",
            Now.AddDays(-10), Now.AddDays(80));
        _borrowing.Summaries[me] = new(me, [loan], [reservation], 2, [penalty]);

        var result = await Service.GetAsync();

        Assert.Equal(new MyProfileDto(me, "Anna", "Petrosyan", "anna.petrosyan@ameriabank.am", "Retail Banking",
            "Analyst", "Employee", "Active"), result.Profile);
        Assert.Equal(new MyLoanDto(loan.LoanId, BookA, "Risk Management Basics", "BK-0001-01", loan.IssuedAtUtc,
            loan.DueAtUtc, false, 0), Assert.Single(result.ActiveLoans));
        Assert.Equal(new MyReservationDto(reservation.ReservationId, BookB, "Clean Code in C#", "ReadyForPickup", 0,
            reservation.ReadyUntilUtc, reservation.CreatedAtUtc), Assert.Single(result.Reservations));
        Assert.Equal(2, result.ActivePenaltyPoints);
        Assert.Equal(new MyPenaltyDto(penalty.PenaltyId, BookB, "Clean Code in C#", 2, "Returned late",
            penalty.CreatedAtUtc, penalty.ExpiresAtUtc), Assert.Single(result.ActivePenalties));
    }

    [Theory]
    [InlineData(LoanStatus.Active, 1, false)]
    [InlineData(LoanStatus.Active, -1, true)]   // due date passed, not marked Overdue yet
    [InlineData(LoanStatus.Overdue, -1, true)]
    public async Task Overdue_flag_follows_the_status_and_the_due_date(LoanStatus status, int dueInDays, bool overdue)
    {
        var me = _user.StaffMemberId!.Value;
        _borrowing.Summaries[me] = new(me, [Loan(me, CopyA, BookA, Now.AddDays(dueInDays), status)], [], 0, []);

        var result = await Service.GetAsync();

        Assert.Equal(overdue, Assert.Single(result.ActiveLoans).IsOverdue);
    }

    [Fact]
    public async Task Reads_only_the_callers_own_member()
    {
        _members.Add(Guid.NewGuid());

        await Service.GetAsync();

        Assert.Equal(_user.StaffMemberId, Assert.Single(_borrowing.Requested));
    }

    [Fact]
    public async Task Token_without_a_staff_profile_is_not_found_without_reading_borrowing()
    {
        _user.StaffMemberId = null;

        var error = await Assert.ThrowsAsync<DomainException>(() => Service.GetAsync());

        Assert.Equal(StaffErrors.NotFound, error.Error);
        Assert.Empty(_borrowing.Requested);
    }

    [Fact]
    public async Task Unknown_member_is_not_found_without_reading_borrowing()
    {
        _user.StaffMemberId = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<DomainException>(() => Service.GetAsync());

        Assert.Equal(StaffErrors.NotFound, error.Error);
        Assert.Empty(_borrowing.Requested);
    }

    [Fact]
    public async Task Book_or_copy_unknown_to_Catalog_gives_null_title_and_barcode()
    {
        var me = _user.StaffMemberId!.Value;
        _borrowing.Summaries[me] = new(me, [Loan(me, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(3))], [], 0, []);

        var loan = Assert.Single((await Service.GetAsync()).ActiveLoans);

        Assert.Null(loan.Title);
        Assert.Null(loan.Barcode);
    }

    private sealed class Members : IStaffManagementRepository
    {
        private readonly Dictionary<Guid, StaffMemberDto> _items = [];

        public void Add(Guid id) => _items[id] = new StaffMemberDto(id, "EMP-1", "Anna", "Petrosyan",
            "anna.petrosyan@ameriabank.am", Guid.NewGuid(), "Retail Banking", "Analyst", null, Guid.NewGuid(),
            "Employee", "Active", Now.AddYears(-1), StaffRoles.StaffMember);

        public Task<StaffMemberDto?> GetDetailsAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(_items.GetValueOrDefault(id));

        public Task<StaffMember?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> EmployeeIdExistsAsync(string employeeId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> EmailExistsAsync(string email, Guid? exceptMemberId, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetDepartmentNameAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> GetCategoryNameAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Department>> GetDepartmentsByNamesAsync(IReadOnlyCollection<string> names,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<StaffCategory>> GetCategoriesByNamesAsync(IReadOnlyCollection<string> names,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<string>> GetExistingEmployeeIdsAsync(IReadOnlyCollection<string> employeeIds,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<string>> GetExistingEmailsAsync(IReadOnlyCollection<string> emails,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetDepartmentNamesAsync(IReadOnlyCollection<Guid> ids,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetCategoryNamesAsync(IReadOnlyCollection<Guid> ids,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedResult<StaffMemberListItemDto>> SearchAsync(StaffMemberSearchFilter filter, PageRequest paging,
            CancellationToken ct) => throw new NotSupportedException();
        public void Add(StaffMember member) => throw new NotSupportedException();
    }

    private sealed class Borrowing : IBorrowingReadService
    {
        public Dictionary<Guid, StaffBorrowingSummary> Summaries { get; } = [];
        public List<Guid> Requested { get; } = [];

        public Task<StaffBorrowingSummary> GetStaffSummaryAsync(Guid staffMemberId, CancellationToken ct = default)
        {
            Requested.Add(staffMemberId);
            return Task.FromResult(Summaries.GetValueOrDefault(staffMemberId) ?? new(staffMemberId, [], [], 0, []));
        }

        public Task<IReadOnlyList<OpenLoanInfo>> GetActiveLoansAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException("My library reads the summary only.");

        public Task<IReadOnlyList<OpenLoanInfo>> GetLoansDueOnAsync(DateOnly localDate, CancellationToken ct = default) =>
            throw new NotSupportedException("My library reads the summary only.");
    }

    private sealed class Catalog : ICatalogService
    {
        public Dictionary<Guid, string> Books { get; } = [];
        public Dictionary<Guid, (Guid BookId, string Barcode)> Copies { get; } = [];

        public Task<BookDto?> GetBookAsync(Guid bookId, CancellationToken ct = default) =>
            Task.FromResult(Books.TryGetValue(bookId, out var title)
                ? new BookDto(bookId, title, "9780134685991", 2020, "English", null, Guid.NewGuid(), "IT", null, null,
                    ["Author"], 1, 0, false)
                : null);

        public Task<IReadOnlyList<BookCopyDto>> GetCopiesAsync(IReadOnlyCollection<Guid> copyIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BookCopyDto>>(copyIds.Where(Copies.ContainsKey)
                .Select(id => new BookCopyDto(id, Copies[id].BookId, Copies[id].Barcode, BookCopyStatus.OnLoan,
                    BookCopyCondition.Good, null))
                .ToArray());

        public Task<BookCopyDto?> GetAvailableCopyAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetCopyStatusAsync(Guid copyId, BookCopyStatus status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BookCopyDto?> GetCopyByBarcodeAsync(string barcode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetCopyIdsAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
