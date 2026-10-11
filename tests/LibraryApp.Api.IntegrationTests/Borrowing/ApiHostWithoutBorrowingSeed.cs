using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// The API host with the Catalog and Staff seed but without the Borrowing seed: tests of loans and reads create their own
/// loans and count on staff members without seeded loans, reservations or penalties. The seed itself is tested on the
/// normal host (BorrowingSeedTests).
/// </summary>
public sealed class ApiHostWithoutBorrowingSeed : ApiHostFactory
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        var borrowingSeed = services.Single(d => d.ServiceType == typeof(IModuleSeeder) && d.ImplementationType == typeof(BorrowingSeeder));
        services.Remove(borrowingSeed);
    }
}
