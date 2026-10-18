using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Infrastructure.IntegrationTests.Time;
using LibraryApp.Infrastructure.Jobs.Borrowing;
using LibraryApp.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Jobs;

public sealed class BorrowingDailyJobTests
{
    // 21 October, 00:30 in Yerevan.
    private static readonly DateTime NowUtc =
        new(2026, 10, 20, 20, 30, 0, DateTimeKind.Utc);

    private static DailyJobRun RunContext() => new(
        BorrowingDailyJob.Name,
        new DateOnly(2026, 10, 21),
        LastSuccessfulStartUtc: NowUtc.AddDays(-7));

    private static ServiceProvider BuildServices(TestState state)
    {
        var services = new ServiceCollection();

        services.AddScoped<IBorrowingDailyJobQuery>(
            _ => new RecordingQuery(state));

        services.AddScoped<IBorrowingDailyJobService>(
            _ => new RecordingWorker(state));

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true
            });
    }

    private static BorrowingDailyJob CreateJob(
        ServiceProvider services,
        OverdueFromMode mode = OverdueFromMode.NextLocalDay)
    {
        var time = new ManualTimeProvider(
            new DateTimeOffset(NowUtc));

        var clock = new SystemClock(
            time,
            Options.Create(new TimeZoneOptions
            {
                TimeZoneId = "Asia/Yerevan"
            }));

        // Only the overdue mode is consumed by this job.
        // Full configuration validation has separate tests.
        var options = Options.Create(new BorrowingOptions
        {
            Loans = new LoanOptions
            {
                OverdueFrom = mode
            }
        });

        return new BorrowingDailyJob(
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            options,
            NullLogger<BorrowingDailyJob>.Instance);
    }

    [Theory]
    [InlineData(OverdueFromMode.NextLocalDay)]
    [InlineData(OverdueFromMode.DueTime)]
    public async Task Selection_uses_the_current_cutoff_after_downtime(
        OverdueFromMode mode)
    {
        var state = new TestState();
        using var services = BuildServices(state);

        await CreateJob(services, mode).RunAsync(
            RunContext(),
            CancellationToken.None);

        DateTime expected = mode == OverdueFromMode.NextLocalDay
            ? new DateTime(
                2026, 10, 20, 20, 0, 0, DateTimeKind.Utc)
            : NowUtc;

        Assert.Equal(expected, state.RequestedCutoffUtc);
        Assert.Equal(1, state.LoanQueryCalls);
        Assert.Equal(1, state.StaffQueryCalls);
    }

    [Fact]
    public async Task Empty_candidates_complete_without_creating_workers()
    {
        var state = new TestState();
        using var services = BuildServices(state);

        string summary = await CreateJob(services).RunAsync(
            RunContext(),
            CancellationToken.None);

        Assert.Empty(state.LoanCalls);
        Assert.Empty(state.StaffCalls);
        Assert.Empty(state.WorkerInstances);

        Assert.Contains("Overdue loans: 0", summary);
        Assert.Contains("expired penalties: 0", summary);
        Assert.Contains("changed statuses: 0", summary);
        Assert.Contains("failures: 0", summary);
    }

    [Fact]
    public async Task Every_item_gets_a_fresh_disposed_worker()
    {
        var state = new TestState();

        state.LoanIds.Add(Guid.NewGuid());
        state.LoanIds.Add(Guid.NewGuid());
        state.StaffIds.Add(Guid.NewGuid());

        using var services = BuildServices(state);

        await CreateJob(services).RunAsync(
            RunContext(),
            CancellationToken.None);

        Assert.Equal(state.LoanIds.ToArray(), state.LoanCalls.ToArray());
        Assert.Equal(state.StaffIds.ToArray(), state.StaffCalls.ToArray());

        Assert.Equal(3, state.WorkerInstances.Count);
        Assert.Equal(3, state.WorkerInstances.Distinct().Count());

        Assert.Equal(
            state.WorkerInstances.ToArray(),
            state.DisposedWorkers.ToArray());
    }

    [Fact]
    public async Task Failures_do_not_prevent_later_loans_or_staff_processing()
    {
        Guid failingLoan = Guid.NewGuid();
        Guid successfulLoan = Guid.NewGuid();
        Guid failingStaff = Guid.NewGuid();
        Guid successfulStaff = Guid.NewGuid();

        var state = new TestState();

        state.LoanIds.AddRange(
            new[] { failingLoan, successfulLoan });

        state.StaffIds.AddRange(
            new[] { failingStaff, successfulStaff });

        state.FailingLoans.Add(failingLoan);
        state.FailingStaff.Add(failingStaff);

        using var services = BuildServices(state);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateJob(services).RunAsync(
                    RunContext(),
                    CancellationToken.None));

        // Both lists were fully processed despite the failures.
        Assert.Equal(state.LoanIds.ToArray(), state.LoanCalls.ToArray());
        Assert.Equal(state.StaffIds.ToArray(), state.StaffCalls.ToArray());

        Assert.Contains("failures: 2", exception.Message);
        Assert.Contains("Overdue loans: 1", exception.Message);

        // Failed items also had their scopes disposed.
        Assert.Equal(4, state.DisposedWorkers.Count);
    }

    [Fact]
    public async Task Summary_counts_changes_reported_by_workers()
    {
        Guid firstStaff = Guid.NewGuid();
        Guid secondStaff = Guid.NewGuid();

        var state = new TestState();

        state.LoanIds.Add(Guid.NewGuid());

        state.StaffIds.AddRange(
            new[] { firstStaff, secondStaff });

        state.StaffResults[firstStaff] =
            new PenaltyRecalculationResult(3, 0, true);

        state.StaffResults[secondStaff] =
            new PenaltyRecalculationResult(2, 7, false);

        using var services = BuildServices(state);

        string summary = await CreateJob(services).RunAsync(
            RunContext(),
            CancellationToken.None);

        Assert.Contains("Overdue loans: 1", summary);
        Assert.Contains("expired penalties: 5", summary);
        Assert.Contains("changed statuses: 1", summary);
        Assert.Contains("failures: 0", summary);
    }

    [Fact]
    public async Task Cancellation_stops_before_processing_the_next_item()
    {
        Guid firstLoan = Guid.NewGuid();
        Guid secondLoan = Guid.NewGuid();

        using var cancellation = new CancellationTokenSource();

        var state = new TestState();

        state.LoanIds.AddRange(
            new[] { firstLoan, secondLoan });

        state.StaffIds.Add(Guid.NewGuid());

        // Cancel as soon as the first worker has processed its loan.
        state.AfterLoan = cancellation.Cancel;

        using var services = BuildServices(state);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateJob(services).RunAsync(
                RunContext(),
                cancellation.Token));

        Assert.Equal(firstLoan, Assert.Single(state.LoanCalls));
        Assert.Empty(state.StaffCalls);

        Assert.Single(state.DisposedWorkers);
    }

    [Fact]
    public async Task An_already_cancelled_run_does_not_query_candidates()
    {
        var state = new TestState();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var services = BuildServices(state);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateJob(services).RunAsync(
                RunContext(),
                cancellation.Token));

        Assert.Equal(0, state.LoanQueryCalls);
        Assert.Equal(0, state.StaffQueryCalls);
        Assert.Empty(state.WorkerInstances);
    }

    private sealed class TestState
    {
        public List<Guid> LoanIds { get; } = new();
        public List<Guid> StaffIds { get; } = new();

        public List<Guid> LoanCalls { get; } = new();
        public List<Guid> StaffCalls { get; } = new();

        public List<Guid> WorkerInstances { get; } = new();
        public List<Guid> DisposedWorkers { get; } = new();

        public HashSet<Guid> FailingLoans { get; } = new();
        public HashSet<Guid> FailingStaff { get; } = new();

        public Dictionary<Guid, PenaltyRecalculationResult>
            StaffResults { get; } = new();

        public DateTime? RequestedCutoffUtc { get; set; }

        public int LoanQueryCalls { get; set; }
        public int StaffQueryCalls { get; set; }

        public Action? AfterLoan { get; set; }
    }

    private sealed class RecordingQuery(TestState state)
        : IBorrowingDailyJobQuery
    {
        public Task<IReadOnlyList<Guid>> GetActiveLoanIdsDueBeforeAsync(
            DateTime cutoffUtc,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            state.LoanQueryCalls++;
            state.RequestedCutoffUtc = cutoffUtc;

            return Task.FromResult<IReadOnlyList<Guid>>(
                state.LoanIds.ToArray());
        }

        public Task<IReadOnlyList<Guid>> GetStaffIdsWithPenaltyHistoryAsync(
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            state.StaffQueryCalls++;

            return Task.FromResult<IReadOnlyList<Guid>>(
                state.StaffIds.ToArray());
        }
    }

    private sealed class RecordingWorker
        : IBorrowingDailyJobService, IDisposable
    {
        private readonly TestState _state;
        private readonly Guid _instanceId = Guid.NewGuid();

        public RecordingWorker(TestState state)
        {
            _state = state;
            _state.WorkerInstances.Add(_instanceId);
        }

        public Task<bool> MarkLoanOverdueAsync(
            Guid loanId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            _state.LoanCalls.Add(loanId);

            if (_state.FailingLoans.Contains(loanId))
            {
                throw new InvalidOperationException(
                    "Simulated loan failure.");
            }

            _state.AfterLoan?.Invoke();

            return Task.FromResult(true);
        }

        public Task<PenaltyRecalculationResult> RecalculateStaffAsync(
            Guid staffMemberId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            _state.StaffCalls.Add(staffMemberId);

            if (_state.FailingStaff.Contains(staffMemberId))
            {
                throw new InvalidOperationException(
                    "Simulated staff failure.");
            }

            PenaltyRecalculationResult result =
                _state.StaffResults.TryGetValue(
                    staffMemberId,
                    out var configured)
                    ? configured
                    : new PenaltyRecalculationResult(0, 0, false);

            return Task.FromResult(result);
        }

        public void Dispose()
        {
            _state.DisposedWorkers.Add(_instanceId);
        }
    }
}