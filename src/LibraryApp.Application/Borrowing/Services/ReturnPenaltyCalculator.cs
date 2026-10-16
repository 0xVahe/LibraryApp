using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Domain.Borrowing;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Borrowing.Services;

/// <summary>
/// Calculates penalties for a return without changing or saving any data.
/// </summary>
public sealed class ReturnPenaltyCalculator(
    PenaltyPolicy policy,
    IWorkingDayCalendar calendar,
    IClock clock,
    IOptions<BorrowingOptions> options)
{
    public ReturnPenaltyCalculation Calculate(
        DateTime dueAtUtc,
        DateTime returnedAtUtc,
        bool damaged)
    {
        if (dueAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException(
                "The due time must be UTC.", nameof(dueAtUtc));

        if (returnedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException(
                "The return time must be UTC.", nameof(returnedAtUtc));

        BorrowingOptions settings = options.Value;

        DateOnly dueDate = clock.ToLocalDate(dueAtUtc);
        DateOnly returnDate = clock.ToLocalDate(returnedAtUtc);

        // Use the actual dates, even if the daily job has not yet
        // changed the loan's stored status to Overdue.
        bool isLate = settings.Loans.OverdueFrom switch
        {
            OverdueFromMode.NextLocalDay => returnDate > dueDate,
            OverdueFromMode.DueTime => returnedAtUtc > dueAtUtc,

            _ => throw new InvalidOperationException(
                "Unknown overdue calculation mode.")
        };

        int lateWorkingDays = isLate
            ? calendar.CountWorkingDays(dueDate, returnDate)
            : 0;

        int latePoints = policy.CalculateLatePoints(lateWorkingDays);

        bool recordZeroPoints =
            settings.Penalties.RecordZeroPointLateReturn
            ?? throw new InvalidOperationException(
                "RecordZeroPointLateReturn must be configured.");

        var penalties = new List<ReturnPenaltyItem>();

        ReturnPenaltyItem? latePenalty = null;
        ReturnPenaltyItem? damagePenalty = null;

        if (isLate && (latePoints > 0 || recordZeroPoints))
        {
            latePenalty = new ReturnPenaltyItem(
                latePoints,
                $"Late return: {lateWorkingDays} overdue working day(s).");
        }

        if (damaged)
        {
            damagePenalty = new ReturnPenaltyItem(
                policy.CalculateDamagedPoints(),
                "Copy returned damaged.");
        }

        if (latePenalty is not null && damagePenalty is not null)
        {
            switch (settings.Penalties.LateAndDamaged)
            {
                case LateAndDamagedMode.Sum:
                    penalties.Add(latePenalty);
                    penalties.Add(damagePenalty);
                    break;

                case LateAndDamagedMode.Highest:
                    penalties.Add(new ReturnPenaltyItem(
                        Math.Max(latePenalty.Points, damagePenalty.Points),
                        $"Late and damaged return: " +
                        $"{lateWorkingDays} overdue working day(s); " +
                        "only the higher penalty applied."));
                    break;

                default:
                    throw new InvalidOperationException(
                        "Unknown late-and-damaged penalty mode.");
            }
        }
        else
        {
            if (latePenalty is not null)
                penalties.Add(latePenalty);

            if (damagePenalty is not null)
                penalties.Add(damagePenalty);
        }

        return new ReturnPenaltyCalculation(
            lateWorkingDays,
            penalties.ToArray());
    }
}

/// <summary>
/// Calculated points and reason for one penalty record.
/// </summary>
public sealed record ReturnPenaltyItem(
    int Points,
    string Reason);

/// <summary>
/// The working-day count and penalties resulting from a return.
/// </summary>
public sealed record ReturnPenaltyCalculation(
    int OverdueWorkingDays,
    IReadOnlyList<ReturnPenaltyItem> Penalties)
{
    public int TotalPoints => Penalties.Sum(penalty => penalty.Points);
}