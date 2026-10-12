using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Dto;

/// <summary>Normalized staff search: trimmed text and a validated status.</summary>
public sealed record StaffMemberSearchFilter(
    string? Text,
    Guid? DepartmentId,
    StaffMemberStatus? Status);