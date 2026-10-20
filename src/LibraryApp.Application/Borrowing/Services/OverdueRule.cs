using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Options;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// The one answer to "is this loan overdue at this moment" (BQ-BA-05, setting <c>Borrowing:Loans:OverdueFrom</c>).
/// <c>NextLocalDay</c>: from the local day after the due date (the whole due date is allowed); <c>DueTime</c>: from the exact
/// due time. Used by the return (late points) and by the daily job that marks loans Overdue (G3-04), so a loan the job
/// marks is always late when it comes back. <see cref="Domain.Borrowing.Loan.MarkOverdue"/> only refuses a moment before
/// the due time, which both variants respect.
/// </summary>
public sealed class OverdueRule(IClock clock, IOptions<BorrowingOptions> options)
{
    public bool IsOverdue(DateTime dueAtUtc, DateTime atUtc) => options.Value.Loans.OverdueFrom switch
    {
        OverdueFromMode.NextLocalDay => clock.ToLocalDate(atUtc) > clock.ToLocalDate(dueAtUtc),
        OverdueFromMode.DueTime => atUtc > dueAtUtc,
        _ => throw new InvalidOperationException("Unknown overdue calculation mode."),
    };

    /// <summary>
    /// The same rule as a bound for a database query: at <paramref name="atUtc"/> a loan is overdue exactly when its
    /// <c>DueAtUtc</c> is before the returned moment (<c>NextLocalDay</c>: the start of the local day of
    /// <paramref name="atUtc"/>; <c>DueTime</c>: <paramref name="atUtc"/> itself). Lets the loan history show and filter an
    /// Active loan past its due date as Overdue before the daily job marks it.
    /// </summary>
    public DateTime OverdueIfDueBeforeUtc(DateTime atUtc) => options.Value.Loans.OverdueFrom switch
    {
        OverdueFromMode.NextLocalDay => clock.StartOfLocalDayUtc(clock.ToLocalDate(atUtc)),
        OverdueFromMode.DueTime => atUtc,
        _ => throw new InvalidOperationException("Unknown overdue calculation mode."),
    };
}
