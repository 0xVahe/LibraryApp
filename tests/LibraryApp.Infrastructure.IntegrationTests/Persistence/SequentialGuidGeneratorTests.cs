using System.Collections.Concurrent;
using System.Data.SqlTypes;
using LibraryApp.Infrastructure.Persistence;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// The start time of each generator is fixed through <see cref="TimeProvider"/>, so "a restart" is simulated exactly
/// (no sleeping, no dependence on the real clock). One tick = 100 ns.
/// </summary>
public class SequentialGuidGeneratorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static SequentialGuidGenerator StartedAt(DateTimeOffset moment) => new(new FixedTimeProvider(moment));

    private static bool IsAfterInSqlServerOrder(Guid later, Guid earlier) => new SqlGuid(later).CompareTo(new SqlGuid(earlier)) > 0;

    [Fact]
    public void Ids_of_one_process_are_unique_and_grow_in_SQL_Server_sort_order()
    {
        var generator = StartedAt(Start);

        var ids = Enumerable.Range(0, 10_000).Select(_ => generator.NewId()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        for (var i = 1; i < ids.Count; i++)
            Assert.True(IsAfterInSqlServerOrder(ids[i], ids[i - 1]), $"id #{i} is not greater than the previous one");
    }

    [Fact]
    public void After_a_restart_ids_continue_above_the_previous_process_when_more_ticks_passed_than_ids_were_issued()
    {
        const int issued = 1_000;
        var before = StartedAt(Start);
        var previousIds = Enumerable.Range(0, issued).Select(_ => before.NewId()).ToList();

        // The new process starts just past the last counter value of the previous one.
        var after = StartedAt(Start.AddTicks(issued + 1));
        var firstAfterRestart = after.NewId();

        Assert.All(previousIds, id => Assert.True(IsAfterInSqlServerOrder(firstAfterRestart, id)));
    }

    [Fact]
    public void After_a_restart_with_the_clock_set_back_ids_stay_unique_though_order_is_not_guaranteed()
    {
        var before = StartedAt(Start);
        var previousIds = Enumerable.Range(0, 10_000).Select(_ => before.NewId()).ToHashSet();

        // Same counter range again: only the random part keeps the ids apart.
        var after = StartedAt(Start);
        var newIds = Enumerable.Range(0, 10_000).Select(_ => after.NewId()).ToList();

        Assert.DoesNotContain(newIds, previousIds.Contains);
    }

    [Fact]
    public void Ids_are_unique_when_many_threads_ask_at_once()
    {
        var generator = StartedAt(Start);
        var ids = new ConcurrentBag<Guid>();

        Parallel.For(0, 20_000, _ => ids.Add(generator.NewId()));

        Assert.Equal(20_000, ids.Distinct().Count());
    }

    [Fact]
    public void Never_returns_an_empty_id()
    {
        Assert.NotEqual(Guid.Empty, StartedAt(Start).NewId());
    }
}
