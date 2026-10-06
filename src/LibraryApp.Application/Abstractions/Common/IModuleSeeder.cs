namespace LibraryApp.Application.Abstractions.Common;

/// <summary>
/// Demo data of one module (SD-17). Rules: idempotent (a second run adds nothing), synthetic data only (C-2),
/// dates relative to <see cref="IClock.UtcNow"/>, constant IDs for anything other modules reference.
/// Order: Catalog 10, Staff 20, Borrowing 30, Notifications 40.
/// </summary>
public interface IModuleSeeder
{
    int Order { get; }

    string Module { get; }

    Task SeedAsync(CancellationToken ct);
}
