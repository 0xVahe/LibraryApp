using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Identity;

/// <summary>
/// Overrides three platform defaults for the Identity tables:
/// cascade inside the account (a user's or role's claims, logins, tokens and role links go with it),
/// 128-character key columns, because two 256-character nvarchar columns exceed SQL Server's 900-byte key limit, and
/// a unique <c>EmailIndex</c>, so two enrolments with the same email at the same time cannot both succeed.
/// The three account roles are model data (<see cref="StaffRoleIds"/>), so they exist in every environment.
/// </summary>
public sealed class IdentityConfiguration :
    IEntityTypeConfiguration<AppUser>,
    IEntityTypeConfiguration<IdentityRole<Guid>>,
    IEntityTypeConfiguration<IdentityUserLogin<Guid>>,
    IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public const int KeyLength = 128;

    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.HasMany<IdentityUserClaim<Guid>>().WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<IdentityUserLogin<Guid>>().WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<IdentityUserToken<Guid>>().WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        // Identity's EmailIndex is not unique; RequireUniqueEmail is only checked in code. Filtered like UserNameIndex.
        builder.HasIndex(x => x.NormalizedEmail)
            .HasDatabaseName("EmailIndex")
            .IsUnique()
            .HasFilter("[NormalizedEmail] IS NOT NULL");
    }

    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<IdentityRoleClaim<Guid>>().WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);

        // Fixed values only: a generated ConcurrencyStamp would change the model on every build (model-sync).
        // NormalizedName as Identity's default normalizer writes it (upper invariant), so FindByNameAsync finds them.
        builder.HasData(StaffRoleIds.All.Select(role => new IdentityRole<Guid>
        {
            Id = role.Id,
            Name = role.Name,
            NormalizedName = role.Name.ToUpperInvariant(),
            ConcurrencyStamp = role.Id.ToString(),
        }));
    }

    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder)
    {
        builder.Property(x => x.LoginProvider).HasMaxLength(KeyLength);
        builder.Property(x => x.ProviderKey).HasMaxLength(KeyLength);
    }

    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
    {
        builder.Property(x => x.LoginProvider).HasMaxLength(KeyLength);
        builder.Property(x => x.Name).HasMaxLength(KeyLength);
    }
}