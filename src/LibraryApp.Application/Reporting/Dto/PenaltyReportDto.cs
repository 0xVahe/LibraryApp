namespace LibraryApp.Application.Reporting.Dto;

public sealed record PenaltyReportDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTime GeneratedAtUtc,
    IReadOnlyList<DepartmentPenaltiesDto> Departments);

/// <summary>
/// Penalty records created in the period. Active points still count now: not waived and not past their expiry.
/// </summary>
public sealed record DepartmentPenaltiesDto(
    Guid DepartmentId,
    string Department,
    int Penalties,
    int IssuedPoints,
    int WaivedPoints,
    int ActivePoints);
