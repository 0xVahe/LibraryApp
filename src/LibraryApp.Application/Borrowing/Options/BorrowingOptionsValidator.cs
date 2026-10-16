using LibraryApp.Domain.Borrowing;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Borrowing.Options;

/// <summary>
/// Checks the <c>Borrowing</c> section at start. A missing value binds as 0 and fails here, so no rule silently runs
/// with a value nobody chose; inconsistent thresholds (Restricted at or above Suspended) fail the same way.
/// </summary>
public sealed class BorrowingOptionsValidator : IValidateOptions<BorrowingOptions>
{
    private const string Penalties = BorrowingOptions.SectionName + ":" + nameof(BorrowingOptions.Penalties);
    private const string Reservations = BorrowingOptions.SectionName + ":" + nameof(BorrowingOptions.Reservations);
    private const string Loans =
        BorrowingOptions.SectionName + ":" + nameof(BorrowingOptions.Loans);
    public ValidateOptionsResult Validate(string? name, BorrowingOptions options)
    {
        var failures = new List<string>();
        PenaltyOptions penalties = options.Penalties;
        ReservationOptions reservations = options.Reservations;

        // Each penalty record holds at most PenaltyRecord.MaxPoints (schema CHECK).
        RequireRange(failures, Penalties, nameof(penalties.PointsPerLateWorkingDay), penalties.PointsPerLateWorkingDay, 1, PenaltyRecord.MaxPoints);
        RequireRange(failures, Penalties, nameof(penalties.MaxLatePoints), penalties.MaxLatePoints, 1, PenaltyRecord.MaxPoints);
        RequireRange(failures, Penalties, nameof(penalties.DamagedPoints), penalties.DamagedPoints, 1, PenaltyRecord.MaxPoints);
        RequireRange(failures, Penalties, nameof(penalties.LostPoints), penalties.LostPoints, 1, PenaltyRecord.MaxPoints);
        RequireRange(failures, Penalties, nameof(penalties.LifetimeDays), penalties.LifetimeDays, 1, int.MaxValue);
        RequireRange(failures, Penalties, nameof(penalties.SuspensionDays), penalties.SuspensionDays, 1, int.MaxValue);
        RequireRange(failures, Penalties, nameof(penalties.RestrictedFromPoints), penalties.RestrictedFromPoints, 1, int.MaxValue);
        RequireRange(failures, Penalties, nameof(penalties.SuspendedFromPoints), penalties.SuspendedFromPoints, 1, int.MaxValue);

        if (penalties.MaxLatePoints < penalties.PointsPerLateWorkingDay)
            failures.Add($"{Penalties}:{nameof(penalties.MaxLatePoints)} ({penalties.MaxLatePoints}) must not be lower than " +
                         $"{nameof(penalties.PointsPerLateWorkingDay)} ({penalties.PointsPerLateWorkingDay}).");

        if (penalties.RestrictedFromPoints >= penalties.SuspendedFromPoints)
            failures.Add($"{Penalties}:{nameof(penalties.RestrictedFromPoints)} ({penalties.RestrictedFromPoints}) must be lower than " +
                         $"{nameof(penalties.SuspendedFromPoints)} ({penalties.SuspendedFromPoints}).");

        RequireRange(failures, Reservations, nameof(reservations.MaxActive), reservations.MaxActive, 1, int.MaxValue);
        RequireRange(failures, Reservations, nameof(reservations.PickupWorkingDays), reservations.PickupWorkingDays, 1, int.MaxValue);

        if (!Enum.IsDefined(typeof(LateAndDamagedMode), penalties.LateAndDamaged))
        {
            failures.Add(
                $"{Penalties}:{nameof(penalties.LateAndDamaged)} " +
                "must be Sum or Highest.");
        }

        if (penalties.RecordZeroPointLateReturn is null)
        {
            failures.Add(
                $"{Penalties}:{nameof(penalties.RecordZeroPointLateReturn)} " +
                "must be explicitly set to true or false.");
        }

        if (!Enum.IsDefined(typeof(OverdueFromMode), options.Loans.OverdueFrom))
        {
            failures.Add(
                $"{Loans}:{nameof(options.Loans.OverdueFrom)} " +
                "must be NextLocalDay or DueTime.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireRange(List<string> failures, string section, string key, int value, int min, int max)
    {
        if (value < min || value > max)
            failures.Add(max == int.MaxValue
                ? $"{section}:{key} must be {min} or more (found {value}); set it in appsettings.json."
                : $"{section}:{key} must be between {min} and {max} (found {value}); set it in appsettings.json.");
    }
}
