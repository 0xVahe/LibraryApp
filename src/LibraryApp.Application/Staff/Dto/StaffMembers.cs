namespace LibraryApp.Application.Staff.Dto;

/// <summary>Employee and account details for enrollment (MEM-01, MEM-03).</summary>
public sealed record EnrolStaffMemberRequest(
    string EmployeeId,
    string FirstName,
    string LastName,
    string CorporateEmail,
    Guid DepartmentId,
    string Position,
    string? LineManagerEmail,
    Guid StaffCategoryId,
    string Password,
    string? Role = null);

/// <summary>Editable profile fields. EmployeeId, role, status and join date are not changed here.</summary>
public sealed record UpdateStaffMemberRequest(
    string FirstName,
    string LastName,
    string CorporateEmail,
    Guid DepartmentId,
    string Position,
    string? LineManagerEmail,
    Guid StaffCategoryId);

/// <summary>Staff profile returned only to Librarian and LibraryAdministrator callers (NFR-06).</summary>
public sealed record StaffMemberDto(
    Guid Id,
    string EmployeeId,
    string FirstName,
    string LastName,
    string CorporateEmail,
    Guid DepartmentId,
    string DepartmentName,
    string Position,
    string? LineManagerEmail,
    Guid StaffCategoryId,
    string StaffCategoryName,
    string Status,
    DateTime JoinedLibraryAtUtc,
    string Role);
