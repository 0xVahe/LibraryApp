using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Abstractions.Staff;

/// <summary>Answer of <see cref="IStaffMemberService.CanBorrowAsync"/> (INT-03). <see cref="Error"/> is set when not allowed.</summary>
public sealed record EligibilityResult(bool IsAllowed, Error? Error)
{
    public static EligibilityResult Allowed() => new(true, null);

    public static EligibilityResult Denied(Error error) => new(false, error);
}