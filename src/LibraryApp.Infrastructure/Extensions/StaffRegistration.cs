using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence.Repositories.Staff;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Extensions;

public static class StaffRegistrationExtensions
{
    /// <summary>Staff module services, login and JWT authentication. Called from Program.cs after AddLibraryIdentity.</summary>
    public static IServiceCollection AddStaffModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IStaffMemberRepository, StaffMemberRepository>();
        services.AddScoped<IStaffMemberService, StaffMemberService>();

        services.AddScoped<IStaffManagementRepository, StaffManagementRepository>();
        services.AddScoped<IStaffAccountStore, StaffAccountStore>();
        services.AddScoped<IStaffManagementService, StaffManagementService>();
        services.AddScoped<IStaffDeactivationService, StaffDeactivationService>();
        services.AddScoped<IMyLibraryService, MyLibraryService>();

        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IDepartmentService, DepartmentService>();

        services.Configure<CorporateEmailOptions>(configuration.GetSection(CorporateEmailOptions.SectionName));
        services.AddSingleton<CorporateEmailPolicy>();
        services.AddScoped<IUserAccountStore, UserAccountStore>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddLibraryJwtAuthentication(configuration);

        services.AddScoped<IModuleSeeder, StaffSeeder>();

        services.AddScoped<IStaffCategoryRepository, StaffCategoryRepository>();
        services.AddScoped<IStaffCategoryService, StaffCategoryService>();        
        
        return services;
    }
}
