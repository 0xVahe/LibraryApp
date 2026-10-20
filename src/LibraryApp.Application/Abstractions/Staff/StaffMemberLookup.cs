using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Abstractions.Staff;

/// <summary>A staff member as the Loan desk shows them after a search by employee ID or corporate email.</summary>
public sealed record StaffMemberLookup(
    Guid Id,
    string EmployeeId,
    string FullName,
    string CorporateEmail,
    StaffMemberStatus Status);