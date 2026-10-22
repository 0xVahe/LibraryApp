using LibraryApp.Application.Reporting.Dto;
using LibraryApp.Application.Common;

namespace LibraryApp.Application.Reporting.Services;

public interface IReportingService
{
    Task<OverdueLoansReportDto> GetOverdueLoansAsync(
        Guid? departmentId,
        int? minimumDaysOverdue,
        int? maximumDaysOverdue,
	PageRequest paging,
        CancellationToken ct = default);

    Task<DashboardReportDto> GetDashboardAsync(CancellationToken ct = default);

    Task<TopBooksAndCategoriesReportDto> GetTopBooksAndCategoriesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);

    Task<ReadingActivityByDepartmentReportDto> GetReadingActivityByDepartmentAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);

    Task<OverdueLoansReportDto> GetAllOverdueLoansAsync(
        Guid? departmentId,
        int? minimumDaysOverdue,
        int? maximumDaysOverdue,
        CancellationToken ct = default);

    Task<InventoryStatusReportDto> GetInventoryStatusAsync(
        Guid? categoryId,
        CancellationToken ct = default);

    Task<PurchaseCandidatesReportDto> GetPurchaseCandidatesAsync(CancellationToken ct = default);
}
