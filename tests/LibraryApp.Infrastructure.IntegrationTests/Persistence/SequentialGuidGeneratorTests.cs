using System.Collections.Concurrent;
using System.Data.SqlTypes;
using LibraryApp.Infrastructure.Persistence;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

public class SequentialGuidGeneratorTests
{
    [Fact]
    public void Generated_ids_are_unique_and_grow_in_SQL_Server_sort_order()
    {
        var generator = new SequentialGuidGenerator();

        var ids = Enumerable.Range(0, 10_000).Select(_ => generator.NewId()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        for (var i = 1; i < ids.Count; i++)
            Assert.True(new SqlGuid(ids[i]).CompareTo(new SqlGuid(ids[i - 1])) > 0, $"id #{i} is not greater than the previous one");
    }

    [Fact]
    public void A_new_generator_after_a_restart_continues_above_the_previous_ids()
    {
        var before = new SequentialGuidGenerator().NewId();
        Thread.Sleep(5);

        var after = new SequentialGuidGenerator().NewId();

        Assert.True(new SqlGuid(after).CompareTo(new SqlGuid(before)) > 0);
    }

    [Fact]
    public void Ids_are_unique_when_many_threads_ask_at_once()
    {
        var generator = new SequentialGuidGenerator();
        var ids = new ConcurrentBag<Guid>();

        Parallel.For(0, 20_000, _ => ids.Add(generator.NewId()));

        Assert.Equal(20_000, ids.Distinct().Count());
    }

    [Fact]
    public void Never_returns_an_empty_id()
    {
        Assert.NotEqual(Guid.Empty, new SequentialGuidGenerator().NewId());
    }
}
