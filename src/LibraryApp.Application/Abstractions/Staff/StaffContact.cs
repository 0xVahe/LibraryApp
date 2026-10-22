using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Abstractions.Staff;

/// <summary>Who a notice about a staff member goes to (X-08). Both emails are stored in lower case.</summary>
/// <param name="FullName">"First Last".</param>
/// <param name="Email">Corporate email.</param>
/// <param name="LineManagerEmail">Line manager's email, or null when the member has none.</param>
/// <param name="Status">So a caller can leave out Deactivated members without one call per member.</param>
public sealed record StaffContact(
    Guid StaffMemberId,
    string FullName,
    string Email,
    string? LineManagerEmail,
    StaffMemberStatus Status);