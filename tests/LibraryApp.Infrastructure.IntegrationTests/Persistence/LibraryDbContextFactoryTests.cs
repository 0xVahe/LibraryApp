using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

/// <summary>The EF tools must create the context exactly as the application does, without a database.</summary>
public class LibraryDbContextFactoryTests
{
    [Fact]
    public void Design_time_context_uses_SQL_Server_with_retry_and_keeps_migrations_in_Infrastructure()
    {
        using var context = new LibraryDbContextFactory().CreateDbContext([]);

        Assert.True(context.Database.IsSqlServer());
        Assert.True(context.Database.CreateExecutionStrategy().RetriesOnFailure);
        Assert.Equal(typeof(LibraryDbContext).Assembly, context.GetService<IMigrationsAssembly>().Assembly);
    }
}
