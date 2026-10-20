using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Ports;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using LibraryApp.Infrastructure.Jobs.Borrowing;
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
        // Rule values and decided variants (SD-21): section "Borrowing" in appsettings.json, checked at start.
        services.AddOptions<BorrowingOptions>()
            .Bind(configuration.GetSection(BorrowingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<BorrowingOptions>, BorrowingOptionsValidator>();

        // Penalty-point policy (BOR-05, BR-BOR-6): the Domain gets the values, never the configuration.
        services.AddSingleton(provider =>
            new PenaltyPolicy(provider.GetRequiredService<IOptions<BorrowingOptions>>().Value.Penalties.ToRules()));

        // Loans (BOR-01, BOR-02)
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddScoped<IPenaltyRepository, PenaltyRepository>();
        services.AddScoped<IBorrowingDailyJobQuery, BorrowingDailyJobQuery>();
        services.AddScoped<
            IBorrowingDailyJobService,
            BorrowingDailyJobService>();
        // Daily Borrowing maintenance (BOR-06, BR-BOR-6).
        services.AddDailyJob<BorrowingDailyJob>(
            BorrowingDailyJob.Name);
        
        services.AddScoped<OverdueRule>();
        services.AddScoped<ReturnPenaltyCalculator>();
        services.AddScoped<IPenaltyService, PenaltyService>();
        services.AddScoped<ILoanService, LoanService>();
        services.AddScoped<ILoanLostService, LoanLostService>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IReservationService, ReservationService>();

        // Contract for Catalog: a new or repaired copy goes to the reservation queue (BOR-07, BQ-G1-10)
        services.AddScoped<ReservationQueueService>();

        services.AddScoped<IReservationQueueService>(provider =>
            provider.GetRequiredService<ReservationQueueService>());//new
        
        services.AddScoped<ILoanReturnService, LoanReturnService>();//new
        
        // Contract for Staff: open loans for deactivation (BR-MEM-4)
        services.AddScoped<IBorrowingReadService, BorrowingReadService>();

        // Contract for Catalog: next expected return date of a book (CAT-08)
        services.AddScoped<ILoanQueryService, LoanQueryService>();

        // Loan desk: copies held for pickup (BOR-07)
        services.AddScoped<IPickupListService, PickupListService>();

        // Demo data, Development only (SD-17)
        services.AddScoped<IModuleSeeder, BorrowingSeeder>();

        //G3-09
        services.AddScoped<ILoanQueryService, LoanQueryService>();

        return services;
    }
}
