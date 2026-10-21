using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Tests.Borrowing.Fakes;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using LibraryApp.Application.Borrowing.Options;

namespace LibraryApp.Application.Tests.Borrowing;

/// <summary>
/// The shared penalty component (reused by the daily job, lost and waive): records, events and the staff status
/// from <see cref="PenaltyPolicy"/>, with default settings (Restricted from 5, Suspended from 10, 30 days).
/// </summary>
public sealed class PenaltyServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid StaffId = new("20000000-0000-0000-0003-000000000099");

    private readonly InMemoryPenalties _penalties = new();
    private readonly RecordingStaff _staff = new();
    private readonly FakeEventBus _events = new();

    private PenaltyService Service(
        Action<BorrowingOptions>? configure = null)
    {
        var options = BorrowingTestSettings.Create(configure);

        return new PenaltyService(
            _penalties,
            new PenaltyPolicy(options.Value.Penalties.ToRules()),
            new FakeIdGenerator(),
            _events,
            _staff,
            options);
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
    /// After a suspension has ended the employee is Restricted while the points stay at 10 or more; a new
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
    
    [Fact]
    public async Task Expiry_at_the_exact_deadline_restores_Active_only_once()
    {
        var record = PenaltyRecord.Create(
            Guid.NewGuid(),
            StaffId,
            Guid.NewGuid(),
            5,
            "Earlier penalty",
            Now.AddDays(-90),
            Now);

        _penalties.Records.Add(record);
        _staff.Status = StaffMemberStatus.Restricted;

        PenaltyService service = Service();

        PenaltyRecalculationResult first =
            await service.RecalculateStatusAsync(StaffId, Now);

        _events.Commit();

        Assert.Equal(PenaltyRecordStatus.Expired, record.Status);
        Assert.Equal(1, first.ExpiredRecords);
        Assert.Equal(0, first.ActivePointsTotal);
        Assert.True(first.StatusChanged);
        Assert.Equal(StaffMemberStatus.Active, _staff.Status);

        PenaltyRecalculationResult second =
            await service.RecalculateStatusAsync(StaffId, Now);

        _events.Commit();

        Assert.Equal(0, second.ExpiredRecords);
        Assert.Equal(0, second.ActivePointsTotal);
        Assert.False(second.StatusChanged);

        Assert.Equal(1, _staff.SetStatusCalls);

        var changed = Assert.Single(
            _events.Published.OfType<StaffStatusChanged>());

        Assert.Equal(StaffMemberStatus.Restricted, changed.OldStatus);
        Assert.Equal(StaffMemberStatus.Active, changed.NewStatus);

        Assert.Empty(_events.Published.OfType<PenaltyApplied>());
    }

    [Theory]
    [InlineData(-1, StaffMemberStatus.Suspended, false)]
    [InlineData(0, StaffMemberStatus.Restricted, true)]
    [InlineData(1, StaffMemberStatus.Restricted, true)]
    public async Task Suspension_ends_exactly_SuspensionDays_after_the_crossing(
        int ticksFromDeadline,
        StaffMemberStatus expectedStatus,
        bool expectedChange)
    {
        var record = PenaltyRecord.Create(
            Guid.NewGuid(),
            StaffId,
            Guid.NewGuid(),
            10,
            "Penalty that triggered suspension",
            Now.AddDays(-30),
            Now.AddDays(60));

        _penalties.Records.Add(record);
        _staff.Status = StaffMemberStatus.Suspended;

        PenaltyRecalculationResult result =
            await Service().RecalculateStatusAsync(
                StaffId,
                Now.AddTicks(ticksFromDeadline));

        _events.Commit();

        Assert.Equal(expectedStatus, _staff.Status);
        Assert.Equal(expectedChange, result.StatusChanged);
        Assert.Equal(10, result.ActivePointsTotal);
        Assert.Equal(0, result.ExpiredRecords);

        Assert.Equal(
            expectedChange ? 1 : 0,
            _events.Published.OfType<StaffStatusChanged>().Count());
    }

    [Fact]
    public async Task An_ended_suspension_is_not_restarted_on_the_next_run()
    {
        var record = PenaltyRecord.Create(
            Guid.NewGuid(),
            StaffId,
            Guid.NewGuid(),
            10,
            "Earlier suspension",
            Now.AddDays(-31),
            Now.AddDays(59));

        _penalties.Records.Add(record);
        _staff.Status = StaffMemberStatus.Suspended;

        PenaltyService service = Service();

        await service.RecalculateStatusAsync(StaffId, Now);
        _events.Commit();

        PenaltyRecalculationResult second =
            await service.RecalculateStatusAsync(
                StaffId,
                Now.AddDays(1));

        _events.Commit();

        Assert.Equal(StaffMemberStatus.Restricted, _staff.Status);
        Assert.Equal(10, second.ActivePointsTotal);
        Assert.False(second.StatusChanged);
        Assert.Equal(1, _staff.SetStatusCalls);

        Assert.Single(
            _events.Published.OfType<StaffStatusChanged>());
    }

    [Fact]
    public async Task Expiring_penalties_never_reactivates_a_deactivated_member()
    {
        var record = PenaltyRecord.Create(
            Guid.NewGuid(),
            StaffId,
            Guid.NewGuid(),
            5,
            "Earlier penalty",
            Now.AddDays(-90),
            Now);

        _penalties.Records.Add(record);
        _staff.Status = StaffMemberStatus.Deactivated;

        PenaltyRecalculationResult result =
            await Service().RecalculateStatusAsync(StaffId, Now);

        _events.Commit();

        Assert.Equal(PenaltyRecordStatus.Expired, record.Status);
        Assert.Equal(1, result.ExpiredRecords);
        Assert.Equal(0, result.ActivePointsTotal);
        Assert.False(result.StatusChanged);

        Assert.Equal(StaffMemberStatus.Deactivated, _staff.Status);
        Assert.Equal(0, _staff.SetStatusCalls);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Suspension_deadline_uses_the_configured_duration()
    {
        PenaltyService service = Service(options =>
            options.Penalties.SuspensionDays = 14);

        await service.ApplyAsync(
            LoanOfStaff(),
            new[] { new ReturnPenaltyItem(10, "Lost copy") },
            Now);

        _events.Commit();

        var changed = Assert.Single(
            _events.Published.OfType<StaffStatusChanged>());

        Assert.Equal(Now.AddDays(14), changed.EffectiveUntilUtc);
        Assert.Equal(StaffMemberStatus.Suspended, _staff.Status);

        await service.RecalculateStatusAsync(
            StaffId,
            Now.AddDays(14));

        _events.Commit();

        Assert.Equal(StaffMemberStatus.Restricted, _staff.Status);
    }

    [Fact]
    public async Task The_suspension_follows_the_latest_record_that_crossed_the_threshold()
    {
        // An old suspension (expired points) and a new crossing 10 days ago: 6 + 5 points.
        _penalties.Records.Add(PenaltyRecord.Create(Guid.NewGuid(), StaffId, Guid.NewGuid(), 10, "Old",
            Now.AddDays(-200), Now.AddDays(-110)));
        _penalties.Records.Add(PenaltyRecord.Create(Guid.NewGuid(), StaffId, Guid.NewGuid(), 6, "First",
            Now.AddDays(-40), Now.AddDays(50)));
        _penalties.Records.Add(PenaltyRecord.Create(Guid.NewGuid(), StaffId, Guid.NewGuid(), 5, "Crossing",
            Now.AddDays(-10), Now.AddDays(80)));
        _staff.Status = StaffMemberStatus.Suspended;
        PenaltyService service = Service();

        var during = await service.RecalculateStatusAsync(StaffId, Now.AddDays(20).AddTicks(-1));
        Assert.False(during.StatusChanged);
        Assert.Equal(StaffMemberStatus.Suspended, _staff.Status);

        var after = await service.RecalculateStatusAsync(StaffId, Now.AddDays(20));
        _events.Commit();

        Assert.True(after.StatusChanged);
        Assert.Equal(StaffMemberStatus.Restricted, _staff.Status);
        Assert.Equal(StaffMemberStatus.Restricted, Assert.Single(_events.Published.OfType<StaffStatusChanged>()).NewStatus);
    }

    [Fact]
    public async Task Waiving_the_record_that_made_the_threshold_ends_the_suspension()
    {
        _penalties.Records.Add(PenaltyRecord.Create(Guid.NewGuid(), StaffId, Guid.NewGuid(), 6, "First",
            Now.AddDays(-40), Now.AddDays(50)));
        var crossing = PenaltyRecord.Create(Guid.NewGuid(), StaffId, Guid.NewGuid(), 5, "Crossing",
            Now.AddDays(-10), Now.AddDays(80));
        crossing.Waive("Business trip");
        _penalties.Records.Add(crossing);
        _staff.Status = StaffMemberStatus.Suspended;

        var result = await Service().RecalculateStatusAsync(StaffId, Now);

        // 6 points remain: no crossing without the waived record, so no suspension; Restricted by points.
        Assert.True(result.StatusChanged);
        Assert.Equal(6, result.ActivePointsTotal);
        Assert.Equal(StaffMemberStatus.Restricted, _staff.Status);
    }

    [Fact]
    public async Task High_points_without_a_new_crossing_do_not_suspend_a_Restricted_member()
    {
        PriorPoints(10);
        _staff.Status = StaffMemberStatus.Restricted;

        var result = await Service().RecalculateStatusAsync(StaffId, Now.AddDays(31));

        Assert.False(result.StatusChanged);
        Assert.Equal(StaffMemberStatus.Restricted, _staff.Status);
    }

    private sealed class InMemoryPenalties : IPenaltyRepository
    {
        public List<PenaltyRecord> Records { get; } = [];

        public List<PenaltyRecord> Added { get; } = [];

        public Task<IReadOnlyList<PenaltyRecord>> GetForStaffAsync(Guid staffMemberId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PenaltyRecord>>(Records.Where(r => r.StaffMemberId == staffMemberId).ToList());

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

        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> staffMemberIds, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<EligibilityResult> CanBorrowAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LendingRules> GetLendingRulesAsync(Guid staffMemberId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<StaffMemberLookup?> FindAsync(string employeeIdOrEmail, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
