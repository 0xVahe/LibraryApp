using LibraryApp.Application.Abstractions.Borrowing;
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
using Xunit;

namespace LibraryApp.Application.Tests.Borrowing;

public sealed class LoanLostServiceTests
{
    private static readonly DateTime Now =
        new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeLoanRepository _loans = new();
    private readonly InMemoryCatalogService _catalog = new();
    private readonly InMemoryPenalties _penalties = new();
    private readonly RecordingStaff _staff = new();
    private readonly FakeEventBus _events = new();
    private readonly FakeApplicationLock _locks = new();

    private readonly Guid _bookId = Guid.NewGuid();

    private Loan Seed(
        DateTime? due = null,
        LoanStatus status = LoanStatus.Active)
    {
        Loan loan = Loan.Issue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            (due ?? Now.AddDays(1)).AddDays(-14),
            14);

        if (status == LoanStatus.Overdue)
            loan.MarkOverdue(Now);

        if (status == LoanStatus.Returned)
            loan.Return(Now);

        if (status == LoanStatus.Lost)
            loan.MarkLost(Now);

        _loans.Seed(loan);

        _catalog.Seed(new(
            loan.BookCopyId,
            _bookId,
            "LOST-TEST",
            BookCopyStatus.OnLoan,
            BookCopyCondition.Good,
            null));

        return loan;
    }

    private LoanLostService Service(
        Action<BorrowingOptions>? configure = null)
    {
        var options = BorrowingTestSettings.Create(configure);

        var policy = new PenaltyPolicy(
            options.Value.Penalties.ToRules());

        var clock = new TestClock(Now);

        var penalties = new PenaltyService(
            _penalties,
            policy,
            new FakeIdGenerator(),
            _events,
            _staff,
            options);

        return new LoanLostService(
            _loans,
            _catalog,
            penalties,
            policy,
            new FakeUnitOfWork(_loans, _catalog, _events),
            _locks,
            clock,
            new OverdueRule(clock, options),
            options);
    }

    [Theory]
    [InlineData(LoanStatus.Active)]
    [InlineData(LoanStatus.Overdue)]
    public async Task Lost_closes_copy_applies_ten_points_and_suspends(
        LoanStatus status)
    {
        Loan loan = Seed(
            status == LoanStatus.Overdue ? Now.AddDays(-1) : null,
            status);

        var result = await Service().DeclareLostAsync(loan.Id);

        Assert.Equal(LoanStatus.Lost, loan.Status);
        Assert.Equal(Now, loan.ReturnedAtUtc);

        Assert.Equal(loan.Id, result.LoanId);
        Assert.Equal(Now, result.DeclaredAtUtc);
        Assert.Equal(LoanStatus.Lost, result.Status);
        Assert.Equal(10, result.PenaltyPointsApplied);

        Assert.Equal(
            10,
            Assert.Single(_penalties.Added).Points);

        Assert.Equal(
            BookCopyStatus.Lost,
            _catalog.Snapshot()[loan.BookCopyId].Status);

        Assert.Equal(
            StaffMemberStatus.Suspended,
            _staff.Status);

        Assert.Single(
            _events.Published.OfType<PenaltyApplied>());

        var statusChanged = Assert.Single(
            _events.Published.OfType<StaffStatusChanged>());

        Assert.Equal(
            Now.AddDays(30),
            statusChanged.EffectiveUntilUtc);

        Assert.Equal(
            new[]
            {
                $"lock:{BorrowingLocks.Staff(loan.StaffMemberId)}",
                $"lock:{BorrowingLocks.Book(_bookId)}"
            },
            _locks.Log);
    }

    [Fact]
    public async Task Configured_points_are_used_instead_of_ten()
    {
        Loan loan = Seed();

        var result = await Service(
                options => options.Penalties.LostPoints = 5)
            .DeclareLostAsync(loan.Id);

        Assert.Equal(5, result.PenaltyPointsApplied);

        Assert.Equal(
            StaffMemberStatus.Restricted,
            _staff.Status);
    }

    [Theory]
    [InlineData(StaffMemberStatus.Suspended)]
    [InlineData(StaffMemberStatus.Deactivated)]
    public async Task Unchanged_staff_status_does_not_emit_status_event(
        StaffMemberStatus status)
    {
        Loan loan = Seed();

        _staff.Status = status;

        await Service().DeclareLostAsync(loan.Id);

        Assert.Equal(status, _staff.Status);

        Assert.Single(
            _events.Published.OfType<PenaltyApplied>());

        Assert.Empty(
            _events.Published.OfType<StaffStatusChanged>());
    }

    [Theory]
    [InlineData(LoanStatus.Returned)]
    [InlineData(LoanStatus.Lost)]
    public async Task Closed_loans_are_rejected_without_penalties(
        LoanStatus status)
    {
        Loan loan = Seed(status: status);

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => Service().DeclareLostAsync(loan.Id));

        Assert.Equal(
            BorrowingErrors.LoanAlreadyClosed,
            exception.Error);

        Assert.Empty(_penalties.Added);
        Assert.Empty(_events.Published);
        Assert.Equal(0, _catalog.SetStatusCalls);
    }

    [Fact]
    public async Task Missing_loan_is_not_found()
    {
        var exception = await Assert.ThrowsAsync<DomainException>(
            () => Service().DeclareLostAsync(Guid.NewGuid()));

        Assert.Equal(
            BorrowingErrors.LoanNotFound,
            exception.Error);
    }

    [Theory]
    [InlineData(OverdueFromMode.DueTime, 1, false)]
    [InlineData(OverdueFromMode.DueTime, 0, false)]
    [InlineData(OverdueFromMode.DueTime, -1, true)]
    [InlineData(OverdueFromMode.NextLocalDay, -1, false)]
    [InlineData(OverdueFromMode.NextLocalDay, -24, true)]
    public async Task Disabled_early_loss_uses_shared_overdue_rule(
        OverdueFromMode mode,
        int dueHours,
        bool allowed)
    {
        Loan loan = Seed(Now.AddHours(dueHours));

        var service = Service(options =>
        {
            options.Loans.AllowLostBeforeDue = false;
            options.Loans.OverdueFrom = mode;
        });

        if (allowed)
        {
            await service.DeclareLostAsync(loan.Id);

            Assert.Equal(LoanStatus.Lost, loan.Status);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<DomainException>(
                () => service.DeclareLostAsync(loan.Id));

            Assert.Equal(
                BorrowingErrors.LoanNotPastDue,
                exception.Error);

            Assert.Equal(LoanStatus.Active, loan.Status);
            Assert.Empty(_penalties.Added);
            Assert.Equal(0, _catalog.SetStatusCalls);
        }
    }

    [Fact]
    public async Task Already_overdue_is_allowed_even_on_the_due_local_day()
    {
        Loan loan = Seed(
            Now.AddHours(-1),
            LoanStatus.Overdue);

        await Service(
                options => options.Loans.AllowLostBeforeDue = false)
            .DeclareLostAsync(loan.Id);

        Assert.Equal(LoanStatus.Lost, loan.Status);
    }

    [Fact]
    public async Task Repeating_loss_does_not_add_another_penalty()
    {
        Loan loan = Seed();

        var service = Service();

        await service.DeclareLostAsync(loan.Id);

        await Assert.ThrowsAsync<DomainException>(
            () => service.DeclareLostAsync(loan.Id));

        Assert.Single(_penalties.Added);

        Assert.Single(
            _events.Published.OfType<PenaltyApplied>());
    }

    [Fact]
    public async Task Missing_copy_is_rejected_before_closing_loan()
    {
        Loan loan = Seed();

        _catalog.Restore(new());

        var exception = await Assert.ThrowsAsync<DomainException>(
            () => Service().DeclareLostAsync(loan.Id));

        Assert.Equal(
            BorrowingErrors.CopyNotFound,
            exception.Error);

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Empty(_penalties.Added);
    }

    [Fact]
    public async Task Cancellation_before_execution_has_no_effect()
    {
        Loan loan = Seed();

        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service().DeclareLostAsync(
                loan.Id,
                cancellation.Token));

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Empty(_penalties.Added);
    }

    private sealed class InMemoryPenalties : IPenaltyRepository
    {
        public List<PenaltyRecord> Records { get; } = [];

        public List<PenaltyRecord> Added { get; } = [];

        public Task<IReadOnlyList<PenaltyRecord>> GetForStaffAsync(
            Guid staffMemberId,
            CancellationToken ct = default)
        {
            IReadOnlyList<PenaltyRecord> records = Records
                .Where(record => record.StaffMemberId == staffMemberId)
                .ToList();

            return Task.FromResult(records);
        }

        public Task<PenaltyRecord?> FindAsync(Guid penaltyId, CancellationToken ct = default) =>
            Task.FromResult(Records.SingleOrDefault(r => r.Id == penaltyId));

        public Task<PenaltyRecord?> GetForUpdateAsync(Guid penaltyId, CancellationToken ct = default) =>
            Task.FromResult(Records.SingleOrDefault(r => r.Id == penaltyId));

        public Task<LibraryApp.Application.Common.PagedResult<PenaltyRecord>> PageForStaffAsync(
            Guid staffMemberId,
            PenaltyRecordStatus? status,
            LibraryApp.Application.Common.PageRequest page,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task AddAsync(
            PenaltyRecord penalty,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            Added.Add(penalty);
            Records.Add(penalty);

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStaff : IStaffMemberService
    {
        public StaffMemberStatus Status { get; set; } =
            StaffMemberStatus.Active;

        public int SetStatusCalls { get; private set; }

        public Task<StaffMemberStatus> GetStatusAsync(
            Guid staffMemberId,
            CancellationToken ct = default) =>
            Task.FromResult(Status);

        public Task SetStatusAsync(
            Guid staffMemberId,
            StaffMemberStatus status,
            string reason,
            CancellationToken ct = default)
        {
            SetStatusCalls++;
            Status = status;

            return Task.CompletedTask;
        }

        public Task<StaffMemberLookup?> FindAsync(
            string employeeIdOrEmail,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            IReadOnlyCollection<Guid> staffMemberIds,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EligibilityResult> CanBorrowAsync(
            Guid staffMemberId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LendingRules> GetLendingRulesAsync(
            Guid staffMemberId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<StaffContact?> GetContactAsync(
            Guid staffMemberId, 
            CancellationToken ct = default) => 
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, StaffContact>> GetContactsAsync(
            IReadOnlyCollection<Guid> staffMemberIds, 
            CancellationToken ct = default) => 
            throw new NotSupportedException();
    }
}
