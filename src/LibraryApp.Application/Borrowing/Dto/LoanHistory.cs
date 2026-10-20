using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>
/// One loan in the history (BOR-10): every status, with the return or lost date when there is one. <c>Status</c> is the
/// effective one: an Active loan past the overdue rule (<c>Borrowing:Loans:OverdueFrom</c>) is Overdue before the daily job
/// marks it, so the status filter and the status shown always agree.
/// </summary>
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
/// <paramref name="IssuedTo"/> are local (Asia/Yerevan) dates, both included (UI guide "Loan history": issued from/to).
/// </summary>
public sealed record LoanHistoryFilter(
    Guid? StaffMemberId,
    Guid? BookId,
    LoanStatus? Status,
    DateOnly? IssuedFrom = null,
    DateOnly? IssuedTo = null);
