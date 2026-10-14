using LibraryApp.Application.Reporting.Dto;

namespace LibraryApp.Application.Reporting.Services;

public interface IReportingService
{
    Task<OverdueLoansReportDto> GetOverdueLoansAsync(
        Guid? departmentId,
        int? minimumDaysOverdue,
        int? maximumDaysOverdue,
        CancellationToken ct = default);

    Task<DashboardReportDto> GetDashboardAsync(CancellationToken ct = default);

    Task<TopBooksAndCategoriesReportDto> GetTopBooksAndCategoriesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);
}
