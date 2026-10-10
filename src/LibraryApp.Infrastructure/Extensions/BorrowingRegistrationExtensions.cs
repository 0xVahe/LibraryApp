using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Extensions;

public static class BorrowingRegistrationExtensions
{
    public static IServiceCollection AddBorrowingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<ILoanService, LoanService>();

        return services;
    }
}