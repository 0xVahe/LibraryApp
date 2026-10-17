namespace LibraryApp.Application.Reporting.Dto;

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
