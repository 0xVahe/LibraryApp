using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Staff.Services;

/// <summary>Staff side of BR-MEM-2 and BR-MEM-3. The loan count is Borrowing's check.</summary>
public static class BorrowingEligibilityPolicy
{
    public static EligibilityResult Evaluate(StaffMemberStatus status) => status switch
    {
        StaffMemberStatus.Active or StaffMemberStatus.Restricted => EligibilityResult.Allowed(),
        StaffMemberStatus.Suspended => EligibilityResult.Denied(StaffErrors.Suspended),
        StaffMemberStatus.Deactivated => EligibilityResult.Denied(StaffErrors.Deactivated),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    // BR-MEM-3 and BR-BOR-1 prohibit renewals for Restricted members.
    public static LendingRules ApplyStatus(
        StaffMemberStatus status, LendingRules categoryRules, int restrictedMaxLoans) =>
        status == StaffMemberStatus.Restricted
            ? categoryRules with { MaxLoans = restrictedMaxLoans, MaxRenewals = 0 }
            : categoryRules;
}
