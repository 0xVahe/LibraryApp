using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Ports;

/// <summary>A tracked staff member together with the names needed to return its profile DTO.</summary>
public sealed record StaffMemberProfile(StaffMember Member, string DepartmentName, string StaffCategoryName);
