using LibraryApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibraryApp.Infrastructure.Persistence.Configurations.Identity;

/// <summary>
/// Overrides two platform defaults for the Identity tables:
/// cascade inside the account (a user's or role's claims, logins, tokens and role links go with it), and
/// 128-character key columns, because two 256-character nvarchar columns exceed SQL Server's 900-byte key limit.
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
    }

    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<IdentityRoleClaim<Guid>>().WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
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