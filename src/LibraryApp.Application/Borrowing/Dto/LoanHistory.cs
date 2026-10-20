using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>One loan in the history (BOR-10): every status, with the return or lost date when there is one.</summary>
public sealed record LoanHistoryItemDto(
    Guid LoanId,
    Guid BookCopyId,
    Guid StaffMemberId,
    DateTime IssuedAtUtc,
    DateTime DueAtUtc,
    DateTime? ReturnedAtUtc,
    int RenewalCount,
    LoanStatus Status);

/// <summary>Filters of the loan history (BOR-10); every filter is optional.</summary>
public sealed record LoanHistoryFilter(Guid? StaffMemberId, Guid? BookId, LoanStatus? Status);
