namespace LibraryApp.Application.Reporting.Dto;

/// <summary>REP-02: the key figures of the library now; cached for <c>Reporting:DashboardCacheMinutes</c>.</summary>
/// <param name="AsOfDate">The day of the report (Asia/Yerevan).</param>
/// <param name="GeneratedAtUtc">When the figures were counted; a cached answer keeps its time.</param>
/// <param name="TotalBooks">Book titles in the catalog, deleted ones not counted.</param>
/// <param name="TotalCopies">Physical copies of the books.</param>
/// <param name="ActiveReaders">Staff members with the Active status.</param>
/// <param name="ActiveLoans">Open loans, overdue ones included.</param>
/// <param name="OverdueLoans">Open loans past their due date.</param>
/// <param name="OverduePercentage">Overdue loans out of all loans, in percent with one decimal.</param>
/// <param name="RestrictedEmployees">Staff members with the Restricted status.</param>
/// <param name="SuspendedEmployees">Staff members with the Suspended status.</param>
public sealed record DashboardReportDto(
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    int TotalBooks,
    int TotalCopies,
    int ActiveReaders,
    int ActiveLoans,
    int OverdueLoans,
    decimal OverduePercentage,
    int RestrictedEmployees,
    int SuspendedEmployees);
