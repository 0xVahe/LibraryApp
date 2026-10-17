using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Borrowing;

/// <summary>
/// The shared penalty component (G3-01; reused by the daily job, lost and waive): records, events and the staff status
/// from <see cref="PenaltyPolicy"/>, with default settings (Restricted from 5, Suspended from 10, 30 days).
/// </summary>
public sealed class PenaltyServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid StaffId = new("20000000-0000-0000-0003-000000000099");

    private readonly InMemoryPenalties _penalties = new();
    private readonly RecordingStaff _staff = new();
    private readonly FakeEventBus _events = new();

    private PenaltyService Service()
    {
        var options = BorrowingTestSettings.Create();
        return new PenaltyService(_penalties, new PenaltyPolicy(options.Value.Penalties.ToRules()), new FakeIdGenerator(),
            _events, _staff, options);
    }

    private static Loan LoanOfStaff() =>
        Loan.Issue(Guid.NewGuid(), Guid.NewGuid(), StaffId, Now.AddDays(-30), 21);

    private void PriorPoints(int points)
    {
        if (points > 0)
            _penalties.Records.Add(PenaltyRecord.Create(Guid.NewGuid(), StaffId, Guid.NewGuid(), points, "Earlier",
                Now.AddDays(-10), Now.AddDays(80)));
    }

    private async Task Apply(params int[] points)
    {
        await Service().ApplyAsync(LoanOfStaff(), points.Select(p => new ReturnPenaltyItem(p, "Test")).ToList(), Now);
        _events.Commit();
    }

    [Theory]
    [InlineData(0, StaffMemberStatus.Active, 4, StaffMemberStatus.Active)]
    [InlineData(0, StaffMemberStatus.Active, 5, StaffMemberStatus.Restricted)]
    [InlineData(4, StaffMemberStatus.Active, 1, StaffMemberStatus.Restricted)]
    [InlineData(5, StaffMemberStatus.Restricted, 5, StaffMemberStatus.Suspended)]
    [InlineData(0, StaffMemberStatus.Active, 10, StaffMemberStatus.Suspended)]
    public async Task The_status_follows_the_thresholds_of_the_policy(int prior, StaffMemberStatus current, int added,
        StaffMemberStatus expected)
    {
        PriorPoints(prior);
        _staff.Status = current;

        await Apply(added);

        Assert.Equal(expected, _staff.Status);
    }

    [Fact]
    public async Task Crossing_the_suspension_threshold_starts_a_suspension_of_the_configured_days()
    {
        PriorPoints(5);
        _staff.Status = StaffMemberStatus.Restricted;

        await Apply(5);

        var changed = Assert.Single(_events.Published.OfType<StaffStatusChanged>());
        Assert.Equal(StaffMemberStatus.Suspended, changed.NewStatus);
        Assert.Equal(Now.AddDays(30), changed.EffectiveUntilUtc);
    }

    /// <summary>
    /// After a suspension has ended the employee is Restricted while the points stay at 10 or more (BQ-BA-01); a new
    /// penalty does not suspend again, because this operation does not cross the threshold.
    /// </summary>
    [Fact]
    public async Task High_points_after_a_suspension_do_not_suspend_again()
    {
        PriorPoints(10);
        _staff.Status = StaffMemberStatus.Restricted;

        await Apply(3);

        Assert.Equal(StaffMemberStatus.Restricted, _staff.Status);
        Assert.Equal(0, _staff.SetStatusCalls);
        Assert.Empty(_events.Published.OfType<StaffStatusChanged>());
    }

    [Theory]
    [InlineData(StaffMemberStatus.Suspended)]
    [InlineData(StaffMemberStatus.Deactivated)]
    public async Task A_suspended_or_deactivated_status_is_never_changed_here(StaffMemberStatus current)
    {
        _staff.Status = current;

        await Apply(5);

        Assert.Equal(current, _staff.Status);
        Assert.Equal(0, _staff.SetStatusCalls);
    }

    [Fact]
    public async Task A_zero_point_record_is_stored_without_PenaltyApplied()
    {
        await Apply(0);

        Assert.Equal(0, Assert.Single(_penalties.Added).Points);
        Assert.Empty(_events.Published.OfType<PenaltyApplied>());
    }

    [Fact]
    public async Task Every_record_with_points_publishes_PenaltyApplied_with_the_running_total()
    {
        PriorPoints(1);

        await Apply(2, 3);

        Assert.Equal([3, 6], _events.Published.OfType<PenaltyApplied>().Select(e => e.ActivePointsTotal));
    }

    private sealed class InMemoryPenalties : IPenaltyRepository
    {
        public List<PenaltyRecord> Records { get; } = [];

        public List<PenaltyRecord> Added { get; } = [];

        public Task<IReadOnlyList<PenaltyRecord>> GetForStaffAsync(Guid staffMemberId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PenaltyRecord>>(Records.Where(r => r.StaffMemberId == staffMemberId).ToList());

        public Task AddAsync(PenaltyRecord penalty, CancellationToken ct = default)
        {
            Added.Add(penalty);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStaff : IStaffMemberService
    {
        public StaffMemberStatus Status { get; set; } = StaffMemberStatus.Active;

        public int SetStatusCalls { get; private set; }

        public Task<StaffMemberStatus> GetStatusAsync(Guid staffMemberId, CancellationToken ct = default) =>
            Task.FromResult(Status);

        public Task SetStatusAsync(Guid staffMemberId, StaffMemberStatus status, string reason, CancellationToken ct = default)
        {
            SetStatusCalls++;
            Status = status;
            return Task.CompletedTask;
        }

        public Task<EligibilityResult> CanBorrowAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LendingRules> GetLendingRulesAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
