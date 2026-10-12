using LibraryApp.Application.Common;

namespace LibraryApp.Application.Staff.Dto;

/// <summary>Staff search inputs and pagination (MEM-07).</summary>
public sealed record StaffMemberSearchRequest(
    string? Search,
    Guid? DepartmentId,
    string? Status,
    PageRequest Paging);