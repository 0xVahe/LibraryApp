namespace LibraryApp.Application.Reporting.Dto;

/// <summary>REP-06: penalty points of a period by department; every department is listed, also without penalties.</summary>
/// <param name="PeriodStart">The first day of the period (Asia/Yerevan), included.</param>
/// <param name="PeriodEnd">The last day of the period (Asia/Yerevan), included.</param>
/// <param name="GeneratedAtUtc">When the report was made.</param>
/// <param name="Departments">Every department, by name.</param>
public sealed record PenaltyReportDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTime GeneratedAtUtc,
    IReadOnlyList<DepartmentPenaltiesDto> Departments);

/// <summary>
/// Penalty records created in the period for the staff of one department (REP-06). Active points still count now: not
/// waived and not past their expiry.
/// </summary>
/// <param name="DepartmentId">Department id.</param>
/// <param name="Department">Department name.</param>
/// <param name="Penalties">Penalty records created in the period.</param>
/// <param name="IssuedPoints">The points of those records.</param>
/// <param name="WaivedPoints">The points of the records that were waived.</param>
/// <param name="ActivePoints">The points of the records that still count now.</param>
public sealed record DepartmentPenaltiesDto(
    Guid DepartmentId,
    string Department,
    int Penalties,
    int IssuedPoints,
    int WaivedPoints,
    int ActivePoints);
