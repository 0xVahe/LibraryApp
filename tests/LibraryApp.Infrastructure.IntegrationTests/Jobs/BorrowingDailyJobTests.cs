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
        Assert.Contains("expired pickups: 0", summary);
        Assert.Contains("expired penalties: 0", summary);
        Assert.Contains("changed statuses: 0", summary);
        Assert.Contains("cancelled reservations: 0", summary);
        Assert.Contains("failures: 0", summary);
    }

    [Fact]
    public async Task Unservable_reservations_are_checked_per_book_and_per_member_after_the_pickups()
    {
        Guid deletedBook = Guid.NewGuid();
        Guid liveBook = Guid.NewGuid();
        Guid deactivatedMember = Guid.NewGuid();
        Guid activeMember = Guid.NewGuid();

        var state = new TestState();
        state.ReservationIds.Add(Guid.NewGuid());
        state.QueuedBookIds.AddRange(new[] { deletedBook, liveBook });
        state.ReservingStaffIds.AddRange(new[] { deactivatedMember, activeMember });
        state.CancelledForBook[deletedBook] = 3;
        state.CancelledForMember[deactivatedMember] = 2;

        using var services = BuildServices(state);

        string summary = await CreateJob(services).RunAsync(
            RunContext(),
            CancellationToken.None);

        Assert.Equal(state.QueuedBookIds.ToArray(), state.BookCalls.ToArray());
        Assert.Equal(state.ReservingStaffIds.ToArray(), state.MemberCalls.ToArray());

        // The clean-up of reservations that can never be served runs after the pickup expiry: a held copy of an expired pickup is first offered to the queue.
        Assert.Equal(
            new[] { "pickup", "book", "book", "member", "member" },
            state.Steps.ToArray());

        // One fresh worker per candidate: 1 pickup + 2 books + 2 members.
        Assert.Equal(5, state.WorkerInstances.Distinct().Count());
        Assert.Equal(state.WorkerInstances.ToArray(), state.DisposedWorkers.ToArray());

        Assert.Contains("cancelled reservations: 5", summary);
        Assert.Contains("failures: 0", summary);
    }

    [Fact]
    public async Task A_failing_queue_cleanup_does_not_stop_the_others_and_fails_the_run_for_a_retry()
    {
        Guid failingBook = Guid.NewGuid();
        Guid deletedBook = Guid.NewGuid();
        Guid deactivatedMember = Guid.NewGuid();

        var state = new TestState();
        state.QueuedBookIds.AddRange(new[] { failingBook, deletedBook });
        state.ReservingStaffIds.Add(deactivatedMember);
        state.FailingBooks.Add(failingBook);
        state.CancelledForBook[deletedBook] = 1;
        state.CancelledForMember[deactivatedMember] = 1;

        using var services = BuildServices(state);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateJob(services).RunAsync(
                    RunContext(),
                    CancellationToken.None));

        Assert.Equal(state.QueuedBookIds.ToArray(), state.BookCalls.ToArray());
        Assert.Equal(deactivatedMember, Assert.Single(state.MemberCalls));
        Assert.Contains("cancelled reservations: 2", exception.Message);
        Assert.Contains("failures: 1", exception.Message);
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
    public async Task Pickups_past_deadline_at_the_run_time_each_get_a_fresh_disposed_worker()
    {
        var state = new TestState();

        state.ReservationIds.Add(Guid.NewGuid());
        state.ReservationIds.Add(Guid.NewGuid());

        using var services = BuildServices(state);

        string summary = await CreateJob(services).RunAsync(
            RunContext(),
            CancellationToken.None);

        // BOR-11: the deadline is compared with the run time, not with the scheduled local date.
        Assert.Equal(NowUtc, state.RequestedPickupNowUtc);
        Assert.Equal(1, state.ReservationQueryCalls);
        Assert.Equal(
            state.ReservationIds.ToArray(),
            state.ReservationCalls.ToArray());

        Assert.Equal(2, state.WorkerInstances.Distinct().Count());
        Assert.Equal(
            state.WorkerInstances.ToArray(),
            state.DisposedWorkers.ToArray());

        Assert.Contains("expired pickups: 2", summary);
        Assert.Contains("failures: 0", summary);
    }

    [Fact]
    public async Task A_failing_pickup_does_not_stop_the_next_one_and_fails_the_run_for_a_retry()
    {
        Guid failingReservation = Guid.NewGuid();
        Guid successfulReservation = Guid.NewGuid();

        var state = new TestState();

        state.ReservationIds.AddRange(
            new[] { failingReservation, successfulReservation });

        state.FailingReservations.Add(failingReservation);

        using var services = BuildServices(state);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateJob(services).RunAsync(
                    RunContext(),
                    CancellationToken.None));

        Assert.Equal(
            state.ReservationIds.ToArray(),
            state.ReservationCalls.ToArray());

        Assert.Contains("expired pickups: 1", exception.Message);
        Assert.Contains("failures: 1", exception.Message);
        Assert.Equal(2, state.DisposedWorkers.Count);
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

        public List<Guid> ReservationIds { get; } = new();
        public List<Guid> ReservationCalls { get; } = new();
        public HashSet<Guid> FailingReservations { get; } = new();
        public DateTime? RequestedPickupNowUtc { get; set; }

        public List<Guid> QueuedBookIds { get; } = new();
        public List<Guid> ReservingStaffIds { get; } = new();
        public List<Guid> BookCalls { get; } = new();
        public List<Guid> MemberCalls { get; } = new();
        public HashSet<Guid> FailingBooks { get; } = new();
        public Dictionary<Guid, int> CancelledForBook { get; } = new();
        public Dictionary<Guid, int> CancelledForMember { get; } = new();
        public List<string> Steps { get; } = new();

        public Dictionary<Guid, PenaltyRecalculationResult>
            StaffResults { get; } = new();

        public DateTime? RequestedCutoffUtc { get; set; }

        public int LoanQueryCalls { get; set; }
        public int StaffQueryCalls { get; set; }
        public int ReservationQueryCalls { get; set; }

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

        public Task<IReadOnlyList<Guid>> GetStaffIdsWithActivePenaltiesAsync(
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            state.StaffQueryCalls++;

            return Task.FromResult<IReadOnlyList<Guid>>(
                state.StaffIds.ToArray());
        }

        public Task<IReadOnlyList<Guid>> GetReadyReservationIdsPastDeadlineAsync(
            DateTime nowUtc,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            state.ReservationQueryCalls++;
            state.RequestedPickupNowUtc = nowUtc;
            return Task.FromResult<IReadOnlyList<Guid>>(
                state.ReservationIds.ToArray());
        }

        public Task<IReadOnlyList<Guid>> GetBookIdsWithWaitingReservationsAsync(
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Guid>>(
                state.QueuedBookIds.ToArray());
        }

        public Task<IReadOnlyList<Guid>> GetStaffIdsWithActiveReservationsAsync(
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<Guid>>(
                state.ReservingStaffIds.ToArray());
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

        public Task<bool> ExpirePickupAsync(
            Guid reservationId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            _state.ReservationCalls.Add(reservationId);
            _state.Steps.Add("pickup");

            if (_state.FailingReservations.Contains(reservationId))
            {
                throw new InvalidOperationException(
                    "Simulated pickup failure.");
            }

            return Task.FromResult(true);
        }

        public Task<int> CancelWaitingReservationsOfDeletedBookAsync(
            Guid bookId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            _state.BookCalls.Add(bookId);
            _state.Steps.Add("book");

            if (_state.FailingBooks.Contains(bookId))
            {
                throw new InvalidOperationException(
                    "Simulated queue failure.");
            }

            return Task.FromResult(
                _state.CancelledForBook.GetValueOrDefault(bookId));
        }

        public Task<int> CancelReservationsOfDeactivatedMemberAsync(
            Guid staffMemberId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            _state.MemberCalls.Add(staffMemberId);
            _state.Steps.Add("member");

            return Task.FromResult(
                _state.CancelledForMember.GetValueOrDefault(staffMemberId));
        }

        public void Dispose()
        {
            _state.DisposedWorkers.Add(_instanceId);
        }
    }
}
