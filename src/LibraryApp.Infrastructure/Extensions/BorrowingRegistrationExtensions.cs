using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.Extensions;

public static class BorrowingRegistrationExtensions
{
    /// <summary>
    /// Borrowing module (Group 3). Called once from Program.cs. Validators and event handlers are found by the platform.
    /// </summary>
    public static IServiceCollection AddBorrowingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Loans (BOR-01, BOR-02)
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<ILoanService, LoanService>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IReservationService, ReservationService>();

        // Contract for Catalog: a new or repaired copy goes to the reservation queue (BOR-07, BQ-G1-10)
        services.AddScoped<IReservationQueueService, ReservationQueueService>();

        // Contract for Staff: open loans for deactivation (BR-MEM-4)
        services.AddScoped<IBorrowingReadService, BorrowingReadService>();

        // Contract for Catalog: next expected return date of a book (CAT-08)
        services.AddScoped<ILoanQueryService, LoanQueryService>();

        // Demo data, Development only (SD-17)
        services.AddScoped<IModuleSeeder, BorrowingSeeder>();

        return services;
    }
}
