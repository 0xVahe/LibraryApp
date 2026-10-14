using LibraryApp.Application.Reporting.Dto;

namespace LibraryApp.Application.Reporting.Services;

public interface IReportingService
{
    Task<OverdueLoansReportDto> GetOverdueLoansAsync(
        Guid? departmentId,
        int? minimumDaysOverdue,
        int? maximumDaysOverdue,
        CancellationToken ct = default);
}
