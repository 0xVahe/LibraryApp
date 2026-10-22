namespace LibraryApp.Application.Abstractions.Staff;

/// <summary>Who a message about a staff member goes to. Both emails are stored in lower case.</summary>
/// <param name="FullName">"First Last".</param>
/// <param name="LineManagerEmail">Line manager's email, or null when the member has none.</param>
public sealed record StaffContact(Guid Id, string FullName, string CorporateEmail, string? LineManagerEmail);