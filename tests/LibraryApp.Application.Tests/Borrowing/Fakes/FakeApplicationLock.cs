using LibraryApp.Application.Abstractions.Common;

namespace LibraryApp.Application.Tests.Borrowing.Fakes;

/// <summary>Records the lock resources in order, into a log shared with other fakes when the order matters.</summary>
public sealed class FakeApplicationLock(List<string>? log = null) : IApplicationLock
{
    public List<string> Log { get; } = log ?? new();

    public Task AcquireAsync(string resource, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Log.Add($"lock:{resource}");
        return Task.CompletedTask;
    }
}
