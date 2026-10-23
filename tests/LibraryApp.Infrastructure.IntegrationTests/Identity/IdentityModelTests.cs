using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Configurations.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LibraryApp.Infrastructure.IntegrationTests.Identity;

/// <summary>Identity tables in the shared model (X-09). No database is needed: only the model is built.</summary>
public class IdentityModelTests
{
    private const string NoDatabase = "Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True";

    private static readonly Type[] IdentityTypes =
    [
        typeof(AppUser), typeof(IdentityRole<Guid>), typeof(IdentityUserRole<Guid>), typeof(IdentityUserClaim<Guid>),
        typeof(IdentityUserLogin<Guid>), typeof(IdentityUserToken<Guid>), typeof(IdentityRoleClaim<Guid>)
    ];

    [Fact]
    public void Identity_tables_are_in_the_shared_context_with_Guid_keys()
    {
        var model = Model();

        Assert.Equal("AspNetUsers", model.FindEntityType(typeof(AppUser))!.GetTableName());
        Assert.Equal(typeof(Guid), model.FindEntityType(typeof(AppUser))!.FindPrimaryKey()!.Properties.Single().ClrType);
        Assert.Equal(typeof(Guid), model.FindEntityType(typeof(IdentityRole<Guid>))!.FindPrimaryKey()!.Properties.Single().ClrType);
    }

    [Fact]
    public void Identity_child_tables_cascade_from_user_and_role()
    {
        var model = Model();

        var foreignKeys = IdentityTypes
            .SelectMany(t => model.FindEntityType(t)!.GetForeignKeys())
            .ToList();

        Assert.Equal(6, foreignKeys.Count);
        Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior));
    }

    [Fact]
    public void Identity_key_columns_fit_the_SQL_Server_key_limit()
    {
        var model = Model();
        var login = model.FindEntityType(typeof(IdentityUserLogin<Guid>))!;
        var token = model.FindEntityType(typeof(IdentityUserToken<Guid>))!;

        Assert.Equal(IdentityConfiguration.KeyLength, login.FindProperty(nameof(IdentityUserLogin<Guid>.LoginProvider))!.GetMaxLength());
        Assert.Equal(IdentityConfiguration.KeyLength, login.FindProperty(nameof(IdentityUserLogin<Guid>.ProviderKey))!.GetMaxLength());
        Assert.Equal(IdentityConfiguration.KeyLength, token.FindProperty(nameof(IdentityUserToken<Guid>.LoginProvider))!.GetMaxLength());
        Assert.Equal(IdentityConfiguration.KeyLength, token.FindProperty(nameof(IdentityUserToken<Guid>.Name))!.GetMaxLength());
    }
    
    [Fact]
    public void Email_index_is_unique_for_accounts_with_an_email()
    {
        var index = Model().FindEntityType(typeof(AppUser))!.GetIndexes()
            .Single(x => x.Properties.Single().Name == nameof(AppUser.NormalizedEmail));

        Assert.Equal("EmailIndex", index.GetDatabaseName());
        Assert.True(index.IsUnique);
        Assert.Equal("[NormalizedEmail] IS NOT NULL", index.GetFilter());
    }

    private static IModel Model()
    {
        using var context = new LibraryDbContext(new DbContextOptionsBuilder<LibraryDbContext>().UseSqlServer(NoDatabase).Options);
        return context.Model;
    }
}