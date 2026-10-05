using System.Buffers.Binary;
using LibraryApp.Application.Abstractions.Common;

namespace LibraryApp.Infrastructure.Persistence;

/// <summary>
/// Guid values that grow in the order SQL Server sorts <c>uniqueidentifier</c>, so inserts go to the end of the
/// index instead of random places (SD-01, option A). Same layout as EF Core's <c>SequentialGuidValueGenerator</c>:
/// random bytes 0-7, a 64-bit counter in bytes 8-15 (SQL Server compares bytes 10-15 first, then 8-9).
/// The counter starts at the current UTC ticks, so values keep growing across restarts.
/// Register as a singleton. Note: Guid version 7 (.NET 9+) is NOT ordered for SQL Server; changing the algorithm
/// means changing only this class.
/// </summary>
public sealed class SequentialGuidGenerator : IIdGenerator
{
    private long _counter = DateTime.UtcNow.Ticks;

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
