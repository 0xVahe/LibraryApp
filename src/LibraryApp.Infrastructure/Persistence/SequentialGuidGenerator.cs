using System.Buffers.Binary;
using LibraryApp.Application.Abstractions.Common;

namespace LibraryApp.Infrastructure.Persistence;

/// <summary>
/// Guid values that grow in the order SQL Server sorts <c>uniqueidentifier</c>, so inserts go to the end of the
/// index instead of random places (SD-01, option A). Same layout as EF Core's <c>SequentialGuidValueGenerator</c>:
/// random bytes 0-7, a 64-bit counter in bytes 8-15 (SQL Server compares bytes 10-15 first, then 8-9).
/// Register as a singleton. Guid version 7 (.NET 9+) is NOT ordered for SQL Server; changing the algorithm means
/// changing only this class.
/// <para>What is guaranteed:</para>
/// <list type="bullet">
/// <item>Uniqueness: always, also across processes and restarts (counter plus 8 random bytes).</item>
/// <item>Order within one process: always (the counter is incremented atomically).</item>
/// <item>Order across restarts and instances: only while the clock moves forward. The counter starts at the UTC ticks
/// of the moment the generator is created (one tick = 100 ns) and grows by one per id, so a new process continues above
/// the previous one if more ticks have passed than ids were issued (this application issues far fewer than ten million
/// ids per second) and the system clock was not set back.</item>
/// </list>
/// Order only affects index performance. Code must never rely on id order for meaning: use a timestamp for that.
/// </summary>
public sealed class SequentialGuidGenerator(TimeProvider time) : IIdGenerator
{
    private long _counter = time.GetUtcNow().UtcTicks;

    public Guid NewId()
    {
        Span<byte> bytes = stackalloc byte[16];
        Guid.NewGuid().TryWriteBytes(bytes);

        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, Interlocked.Increment(ref _counter));

        // Most significant counter bytes go where SQL Server looks first.
        counter[..6].CopyTo(bytes[10..]);
        counter[6..].CopyTo(bytes[8..]);

        return new Guid(bytes);
    }
}
