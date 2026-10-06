using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Infrastructure.Persistence.Repositories.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Staff;

public static class StaffRegistration
{
    /// <summary>Staff module services. Called from Program.cs.</summary>
    public static IServiceCollection AddStaffModule(this IServiceCollection services)
    {
        services.AddScoped<IStaffMemberRepository, StaffMemberRepository>();
        services.AddScoped<IStaffMemberService, StaffMemberService>();

        return services;
    }
}