using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>
/// One loan in the history (BOR-10): every status, with the return or lost date when there is one. <c>Status</c> is the
/// effective one: an Active loan past the overdue rule (<c>Borrowing:Loans:OverdueFrom</c>) is Overdue before the daily job
/// marks it, so the status filter and the status shown always agree.
/// </summary>
/// <param name="LoanId">The loan.</param>
/// <param name="BookCopyId">The copy that was lent; Catalog gives its book and barcode.</param>
/// <param name="StaffMemberId">The staff member who borrowed it.</param>
/// <param name="IssuedAtUtc">When the copy was issued (UTC).</param>
/// <param name="DueAtUtc">When the copy is or was due back (UTC).</param>
/// <param name="ReturnedAtUtc">When the copy was taken back or declared lost (UTC); null while the loan is open.</param>
/// <param name="RenewalCount">How many times the loan was renewed.</param>
/// <param name="Status">Active, Overdue, Returned or Lost — the effective status.</param>
public sealed record LoanHistoryItemDto(
    Guid LoanId,
    Guid BookCopyId,
    Guid StaffMemberId,
    DateTime IssuedAtUtc,
    DateTime DueAtUtc,
    DateTime? ReturnedAtUtc,
    int RenewalCount,
    LoanStatus Status);

/// <summary>
/// Filters of the loan history (BOR-10); every filter is optional. <paramref name="IssuedFrom"/> and
/// <paramref name="IssuedTo"/> are local (Asia/Yerevan) dates, both included.
/// </summary>
public sealed record LoanHistoryFilter(
    Guid? StaffMemberId,
    Guid? BookId,
    LoanStatus? Status,
    DateOnly? IssuedFrom = null,
    DateOnly? IssuedTo = null);
