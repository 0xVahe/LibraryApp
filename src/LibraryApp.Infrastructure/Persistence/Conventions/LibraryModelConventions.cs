using LibraryApp.Domain.Common;
using LibraryApp.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Persistence.Conventions;

/// <summary>
/// Rules that hold for every table of <see cref="LibraryDbContext"/>. Kept apart from the context so the tests can
/// prove them on probe entities while no module has a table yet.
/// </summary>
public static class LibraryModelConventions
{
    /// <summary>Default length of a string column (SD-12). A configuration sets another length, or -1 for nvarchar(max).</summary>
    public const int DefaultStringLength = 256;

    public static void Configure(ModelConfigurationBuilder configurationBuilder)
    {
        // SD-03 (default): every DateTime in the database is UTC; a local time is refused on save and in queries.
        // The registration for DateTime also covers DateTime? properties.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

        // SD-12: an explicit default instead of nvarchar(max).
        configurationBuilder.Properties<string>().HaveMaxLength(DefaultStringLength);

        // SD-12: Restrict instead of EF Core's cascade for required relationships, so history cannot disappear through
        // a cascade. Cascade only inside one aggregate, written explicitly: .OnDelete(DeleteBehavior.Cascade).
        configurationBuilder.Conventions.Replace<CascadeDeleteConvention>(services =>
            new RestrictDeleteConvention(services.GetRequiredService<ProviderConventionSetBuilderDependencies>()));

        // SD-11 (default): enums are stored as int by EF Core without extra setup. A group that needs a string column
        // writes .HasConversion<string>() in its own configuration.
    }

    /// <summary>
    /// SD-12, applied after all configurations: a relationship that still cascades only because EF Core chose it
    /// (not written explicitly with <c>OnDelete(...)</c>, not an owned type) becomes Restrict. EF Core can choose
    /// cascade after the convention in <see cref="Configure"/> has run (seen for entities without a <c>DbSet</c> in a
    /// context derived from <c>IdentityDbContext</c>); this pass makes the rule independent of that.
    /// </summary>
    public static void ApplyRestrictDeleteDefault(ModelBuilder modelBuilder)
    {
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(e => e.GetDeclaredForeignKeys())
                     .Cast<IConventionForeignKey>())
        {
            if (!foreignKey.IsOwnership
                && foreignKey.DeleteBehavior == DeleteBehavior.Cascade
                && foreignKey.GetDeleteBehaviorConfigurationSource() is null or ConfigurationSource.Convention)
                foreignKey.SetDeleteBehavior(DeleteBehavior.Restrict);
        }
    }

    /// <summary>
    /// SD-01: identifiers come from <c>IIdGenerator</c>, EF must never invent one. Applied to every entity derived from
    /// <see cref="Entity"/>, so no group has to remember <c>ValueGeneratedNever()</c>. Link tables without
    /// <see cref="Entity"/> (composite key) and Identity tables are not touched.
    /// </summary>
    public static void ApplyApplicationGeneratedIds(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(e => typeof(Entity).IsAssignableFrom(e.ClrType)))
        {
            var key = entityType.FindPrimaryKey();
            if (key is { Properties.Count: 1 } && key.Properties[0].ClrType == typeof(Guid))
                key.Properties[0].ValueGenerated = ValueGenerated.Never;
        }
    }

    /// <summary>
    /// EF Core's cascade-delete convention with one change: a relationship deletes nothing by default.
    /// Owned types keep cascade (they are part of the owner's row or aggregate). An explicit
    /// <c>OnDelete(...)</c> in a configuration always wins over this convention.
    /// </summary>
    private sealed class RestrictDeleteConvention(ProviderConventionSetBuilderDependencies dependencies)
        : CascadeDeleteConvention(dependencies)
    {
        protected override DeleteBehavior GetTargetDeleteBehavior(IConventionForeignKey foreignKey) =>
            foreignKey.IsOwnership ? DeleteBehavior.Cascade : DeleteBehavior.Restrict;
    }
}
