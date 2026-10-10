using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Fakes;

/// <summary>An authenticated caller with one role; a Librarian unless a test says otherwise.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid UserId { get; set; } = Guid.NewGuid();
    public Guid? StaffMemberId { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "caller@ameriabank.am";
    public string Role { get; set; } = StaffRoles.Librarian;
    public bool IsInRole(string role) => Role == role;
}