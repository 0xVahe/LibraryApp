using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Borrowing.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LibraryApp.Infrastructure.Extensions;

public static class BorrowingReadRegistrationExtensions
{
    /// <summary>Temporary read provider for S1-04.2, until Group 3 registers its real implementation.</summary>
    public static IServiceCollection AddBorrowingReadModule(this IServiceCollection services)
    {
        services.TryAddScoped<IBorrowingReadService, BorrowingReadServiceStub>();
        return services;
    }
}
