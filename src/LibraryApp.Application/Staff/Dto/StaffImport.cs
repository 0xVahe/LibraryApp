namespace LibraryApp.Application.Staff.Dto;

public static class StaffImportLimits
{
    public const int MaximumRows = 1000;
    public const long MaximumFileBytes = 1_000_000;
}

/// <summary>A logical CSV record. RowNumber is the physical line where the record begins.</summary>
public sealed record StaffImportCsvRow(
    int RowNumber,
    string EmployeeId,
    string FirstName,
    string LastName,
    string CorporateEmail,
    string Department,
    string Position,
    string? LineManagerEmail,
    string Category,
    string? Role);

public sealed record StaffImportError(int RowNumber, string Field, string Message);

public sealed record StaffImportMemberResult(StaffMemberDto Member, string TemporaryPassword);

public sealed record StaffImportResult(IReadOnlyList<StaffImportMemberResult> Members);
