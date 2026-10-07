namespace LibraryApp.Application.Abstractions.Common;

/// <summary>
/// A database lock for rules over a GROUP of rows (for example "at most 3 loans per employee"), which a unique index cannot protect.
/// Held until the end of the current transaction, so it can only be used inside <see cref="IUnitOfWork"/>.
/// Always take locks in the same order (staff, then book, then copy), otherwise two requests can wait for each other forever.
/// </summary>
public interface IApplicationLock
{
    Task AcquireAsync(string resource, CancellationToken ct = default);
}
