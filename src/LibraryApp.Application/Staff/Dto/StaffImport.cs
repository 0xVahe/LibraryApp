namespace LibraryApp.Application.Staff.Dto;

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
