using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>The Borrowing seed without the Staff and Catalog seed: skipped with a warning, nothing written, no error.</summary>
public sealed class BorrowingSeederTests : IAsyncLifetime
{
    private readonly string _connectionString = TestDatabase.NewConnectionString();

    private LibraryDbContext Open()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(_connectionString);
        return new LibraryDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await using var db = Open();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = Open();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Without_the_seeded_staff_members_and_copies_nothing_is_seeded()
    {
        await using var db = Open();
        // The providers are never reached: the seeder checks its references first.
        var seeder = new BorrowingSeeder(db, null!, null!, null!, null!, null!, null!, NullLogger<BorrowingSeeder>.Instance);

        await new UnitOfWork(db).ExecuteInTransactionAsync(seeder.SeedAsync);

        await using var verify = Open();
        Assert.Equal(0, await verify.Loans.CountAsync() + await verify.Reservations.CountAsync() + await verify.PenaltyRecords.CountAsync());
    }
}
