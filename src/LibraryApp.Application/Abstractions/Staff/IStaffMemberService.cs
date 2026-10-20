using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Abstractions.Staff;

/// <summary>
/// What the Staff module offers other modules. Provider: Group 2. Consumer: Borrowing (Group 3), Catalog (Group 1).
/// <para>
/// The loan limit is checked by Borrowing, which owns the loans: it compares its active loan count with
/// <see cref="LendingRules.MaxLoans"/> and reports <see cref="StaffErrors.LoanLimitReached"/>.
/// </para>
/// </summary>
public interface IStaffMemberService
{
    /// <summary>INT-03. Status check only: Active and Restricted may borrow; Suspended, Deactivated and unknown may not.</summary>
    Task<EligibilityResult> CanBorrowAsync(Guid staffMemberId, CancellationToken ct = default);

    /// <summary>INT-04. Restricted: configured Staff:RestrictedLimits override category loan and renewal limits (BR-MEM-3). Throws <c>staff.not_found</c>.</summary>
    Task<LendingRules> GetLendingRulesAsync(Guid staffMemberId, CancellationToken ct = default);

    /// <summary>Current status, so Borrowing changes it only when it differs. Throws <c>staff.not_found</c>.</summary>
    Task<StaffMemberStatus> GetStatusAsync(Guid staffMemberId, CancellationToken ct = default);

    /// <summary>
    /// INT-05. Sets Active, Restricted or Suspended; the same status is a no-op. Never publishes events: the caller
    /// publishes <c>StaffStatusChanged</c>. Only changes data: call it inside the caller's <c>IUnitOfWork</c>.
    /// Throws <c>staff.not_found</c>, <c>staff.status_invalid</c> (Deactivated) and
    /// <c>staff.deactivated_status_locked</c> (member is Deactivated).
    /// </summary>
    Task SetStatusAsync(Guid staffMemberId, StaffMemberStatus status, string reason, CancellationToken ct = default);
    
    /// <summary>
    /// Display names ("First Last") of the given staff members in one query, for lists such as the pickup queue or
    /// overdue loans. Unknown ids are left out of the result (no exception); Deactivated members are included.
    /// An empty list returns an empty dictionary without a query.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        IReadOnlyCollection<Guid> staffMemberIds,
        CancellationToken ct = default);
    
    /// <summary>
    /// Finds one staff member by employee ID or corporate email, for the Loan desk. A value with <c>@</c> is an
    /// email; the search ignores case and spaces around the value. Unknown or empty → <c>null</c> (no exception).
    /// Deactivated members are found too: whether they may borrow is still decided by <see cref="CanBorrowAsync"/>.
    /// </summary>
    Task<StaffMemberLookup?> FindAsync(string employeeIdOrEmail, CancellationToken ct = default);
}
