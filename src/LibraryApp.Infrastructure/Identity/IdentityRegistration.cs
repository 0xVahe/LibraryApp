using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LibraryApp.Infrastructure.Identity;

public static class IdentityRegistration
{
    /// <summary>Identity over LibraryDbContext (X-09) with the password rules of BR-MEM-5. Login and JWT come later.</summary>
    public static IServiceCollection AddLibraryIdentity(this IServiceCollection services)
    {
        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<LibraryDbContext>()
            .AddPasswordValidator<LetterPasswordValidator>();

        services.Replace(ServiceDescriptor.Scoped<IUserStore<AppUser>, StaffUserStore>());

        return services;
    }
}
