namespace LibraryApp.Application.Staff.Dto;

/// <summary>Staff information returned in search results (MEM-07).</summary>
public sealed record StaffMemberListItemDto(
    Guid Id,
    string EmployeeId,
    string FirstName,
    string LastName,
    string CorporateEmail,
    Guid DepartmentId,
    string DepartmentName,
    string Status);