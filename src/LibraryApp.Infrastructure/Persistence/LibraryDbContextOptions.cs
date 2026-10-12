using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence;

/// <summary>
/// The SQL Server options of <see cref="LibraryDbContext"/>, written once. Used by the running application and by
/// <see cref="LibraryDbContextFactory"/> (EF tools), so migrations are created and applied with the same settings.
/// </summary>
public static class LibraryDbContextOptions
{
    public static DbContextOptionsBuilder UseLibrarySqlServer(this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlServer(connectionString, sql => sql
            .MigrationsAssembly(typeof(LibraryDbContext).Assembly.FullName)
            // A short network failure does not fail the request; IUnitOfWork runs the whole operation again.
            .EnableRetryOnFailure());
}
