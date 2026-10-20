using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Staff;
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

/// <summary>G3-03: BR-BOR-1 refusals one by one and in their order, the renewal itself, NFR-03 and the locks.</summary>
public sealed class LoanRenewalServiceTests
{
    // 20 October at 12:00 in Yerevan.
    private static readonly DateTime Now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private readonly Guid _staffId = Guid.NewGuid();
    private readonly Guid _bookId = Guid.NewGuid();
    private readonly Guid _copyId = Guid.NewGuid();

    private readonly FakeLoanRepository _loans = new();
    private readonly InMemoryCatalogService _catalog = new();
    private readonly FakeEventBus _events = new();
    private readonly FakeApplicationLock _locks = new();
    private readonly WaitingQueue _reservations = new();
    private readonly StaffFake _staff = new();
    private readonly FakeCurrentUser _caller = new();
    private readonly FixedYerevanClock _clock = new(Now);

    private LoanRenewalService Service(Action<BorrowingOptions>? settings = null)
    {
        var options = BorrowingTestSettings.Create(settings);
        return new LoanRenewalService(
            _loans, _reservations, _catalog, _staff, new OverdueRule(_clock, options),
            new FakeUnitOfWork(_loans, _catalog, _events), _locks, _events, _caller, _clock, new FakeIdGenerator(), options);
    }

    private Loan SeedLoan(int renewals = 0, DateTime? dueAtUtc = null)
    {
        _catalog.Seed(new BookCopyDto(_copyId, _bookId, "RENEW-001", BookCopyStatus.OnLoan, BookCopyCondition.Good, "A-1"));
        // Issued so that, after the earlier renewals of one day each, the loan is due at dueAtUtc.
        DateTime due = dueAtUtc ?? Now.AddDays(5);
        Loan loan = Loan.Issue(Guid.NewGuid(), _copyId, _staffId, due.AddDays(-21 - renewals), 21);
        for (int i = 0; i < renewals; i++)
            loan.Renew(1);
        _loans.Seed(loan);
        return loan;
    }

    [Fact]
    public async Task A_renewal_moves_the_due_date_by_the_loan_days_under_staff_then_book_lock_and_publishes_LoanRenewed()
    {
        Loan loan = SeedLoan();
        DateTime oldDue = loan.DueAtUtc;

        var result = await Service().RenewAsync(loan.Id);

        Assert.Equal((oldDue.AddDays(21), 1), (result.DueAtUtc, result.RenewalCount));
        Assert.Equal(
            new[] { $"lock:{BorrowingLocks.Staff(_staffId)}", $"lock:{BorrowingLocks.Book(_bookId)}" },
            _locks.Log);

        LoanRenewed renewed = Assert.Single(_events.Published.OfType<LoanRenewed>());
        Assert.Equal((loan.Id, _staffId, _bookId, oldDue.AddDays(21), 1),
            (renewed.LoanId, renewed.StaffMemberId, renewed.BookId, renewed.NewDueAtUtc, renewed.RenewalCount));
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "overdue", BorrowingErrors.LoanOverdueCannotBeRenewed.Code },
        { "past-due", BorrowingErrors.LoanOverdueCannotBeRenewed.Code },
        { "restricted", BorrowingErrors.RestrictedRenewalForbidden.Code },
        { "suspended", BorrowingErrors.SuspendedRenewalForbidden.Code },
        { "max", BorrowingErrors.MaxRenewalsReached.Code },
        { "waiting", BorrowingErrors.RenewalBlockedByReservation.Code },
        { "returned", BorrowingErrors.LoanCannotBeRenewed.Code },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Each_rule_of_BR_BOR_1_refuses_with_its_own_code_and_changes_nothing(string rule, string code)
    {
        Loan loan = SeedLoan(
            renewals: rule == "max" ? 2 : 0,
            dueAtUtc: rule is "overdue" or "past-due" ? Now.AddDays(-2) : null);
        if (rule == "overdue") loan.MarkOverdue(Now);
        if (rule == "returned") loan.Return(Now);
        if (rule == "restricted") _staff.Status = StaffMemberStatus.Restricted;
        if (rule == "suspended") _staff.Status = StaffMemberStatus.Suspended;
        if (rule == "waiting") _reservations.Waiting.Add(Reservation.Create(Guid.NewGuid(), _bookId, Guid.NewGuid(), Now, 1));
        DateTime due = loan.DueAtUtc;
        int count = loan.RenewalCount;

        var error = await Assert.ThrowsAsync<DomainException>(() => Service().RenewAsync(loan.Id));

        Assert.Equal(code, error.Error.Code);
        Assert.Equal((due, count), (loan.DueAtUtc, loan.RenewalCount));
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task The_checks_run_in_order_overdue_status_maximum_queue()
    {
        // Every rule fails at once: the first one in the order wins, then the next once the first is fixed.
        Loan loan = SeedLoan(renewals: 2, dueAtUtc: Now.AddDays(-2));
        _staff.Status = StaffMemberStatus.Restricted;
        _reservations.Waiting.Add(Reservation.Create(Guid.NewGuid(), _bookId, Guid.NewGuid(), Now, 1));

        Assert.Equal(BorrowingErrors.LoanOverdueCannotBeRenewed, (await Assert.ThrowsAsync<DomainException>(() => Service().RenewAsync(loan.Id))).Error);

        _clock.UtcNow = Now.AddDays(-10);   // the loan is not past due any more
        Assert.Equal(BorrowingErrors.RestrictedRenewalForbidden, (await Assert.ThrowsAsync<DomainException>(() => Service().RenewAsync(loan.Id))).Error);

        _staff.Status = StaffMemberStatus.Active;
        Assert.Equal(BorrowingErrors.MaxRenewalsReached, (await Assert.ThrowsAsync<DomainException>(() => Service().RenewAsync(loan.Id))).Error);

        _staff.Rules = _staff.Rules with { MaxRenewals = 3 };
        Assert.Equal(BorrowingErrors.RenewalBlockedByReservation, (await Assert.ThrowsAsync<DomainException>(() => Service().RenewAsync(loan.Id))).Error);
    }

    [Fact]
    public async Task A_suspended_member_renews_when_the_setting_allows_it()
    {
        Loan loan = SeedLoan();
        _staff.Status = StaffMemberStatus.Suspended;

        var result = await Service(settings => settings.Loans.SuspendedMayRenew = true).RenewAsync(loan.Id);

        Assert.Equal(1, result.RenewalCount);
    }

    [Fact]
    public async Task The_borrowers_own_waiting_reservation_does_not_block_the_renewal()
    {
        Loan loan = SeedLoan();
        _reservations.Waiting.Add(Reservation.Create(Guid.NewGuid(), _bookId, _staffId, Now, 1));

        Assert.Equal(1, (await Service().RenewAsync(loan.Id)).RenewalCount);
    }

    [Fact]
    public async Task A_staff_member_renews_their_own_loan_and_gets_404_for_another_persons_before_any_lock()
    {
        Loan loan = SeedLoan();
        _caller.Role = StaffRoles.StaffMember;

        _caller.StaffMemberId = Guid.NewGuid();
        Assert.Equal(BorrowingErrors.LoanNotFound,
            (await Assert.ThrowsAsync<DomainException>(() => Service().RenewAsync(loan.Id))).Error);
        Assert.Empty(_locks.Log);

        _caller.StaffMemberId = _staffId;
        Assert.Equal(1, (await Service().RenewAsync(loan.Id)).RenewalCount);
    }

    [Fact]
    public async Task An_unknown_loan_is_not_found()
    {
        Assert.Equal(BorrowingErrors.LoanNotFound,
            (await Assert.ThrowsAsync<DomainException>(() => Service().RenewAsync(Guid.NewGuid()))).Error);
    }

    /// <summary>Employee rules by default: 21 loan days, 2 renewals.</summary>
    private sealed class StaffFake : IStaffMemberService
    {
        public StaffMemberStatus Status { get; set; } = StaffMemberStatus.Active;
        public LendingRules Rules { get; set; } = new(MaxLoans: 3, LoanDays: 21, MaxRenewals: 2);

        public Task<StaffMemberStatus> GetStatusAsync(Guid staffMemberId, CancellationToken ct = default) => Task.FromResult(Status);
        public Task<LendingRules> GetLendingRulesAsync(Guid staffMemberId, CancellationToken ct = default) => Task.FromResult(Rules);
        public Task<EligibilityResult> CanBorrowAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetStatusAsync(Guid staffMemberId, StaffMemberStatus status, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> staffMemberIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StaffMemberLookup?> FindAsync(string employeeIdOrEmail, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class WaitingQueue : IReservationRepository
    {
        public List<Reservation> Waiting { get; } = [];

        public Task<IReadOnlyList<Reservation>> GetWaitingForBookAsync(Guid bookId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Reservation>>(Waiting.Where(r => r.BookId == bookId).ToList());

        public Task<int> CountActiveAsync(Guid staffMemberId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasActiveForBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountWaitingForBookAsync(Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Reservation?> FindReadyForCopyAsync(Guid copyId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Reservation?> FindActiveForStaffAndBookAsync(Guid staffMemberId, Guid bookId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(Reservation reservation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task FlushAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Reservation?> FindAsync(Guid reservationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Reservation?> GetForUpdateAsync(Guid reservationId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
