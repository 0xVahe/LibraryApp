using LibraryApp.Domain.Staff;

namespace LibraryApp.Domain.Borrowing;

/// <summary>
/// The penalty-point policy (BOR-05, BR-BOR-6): points for late, damaged and lost copies, how long they count and the
/// staff status they give. One policy for every Borrowing use case, the daily job and the seed; the values come from
/// <see cref="PenaltyRules"/>.
/// </summary>
public class PenaltyPolicy(PenaltyRules rules)
{
    public int CalculateLatePoints(int lateWorkingDays)
    {
        if (lateWorkingDays <= 0)
        {
            return 0;
        }

        // long: a very late return must not overflow before the cap applies.
        return (int)Math.Min(
            (long)lateWorkingDays * rules.PointsPerLateWorkingDay,
            rules.MaxLatePoints);
    }

    public int CalculateLostPoints()
    {
        return rules.LostPoints;
    }

    public int CalculateDamagedPoints()
    {
        return rules.DamagedPoints;
    }

    /// <summary>When a penalty record created at this moment stops counting.</summary>
    public DateTime ExpiresAtUtc(DateTime createdAtUtc)
    {
        return createdAtUtc.AddDays(rules.LifetimeDays);
    }

    /// <summary>The status the active penalty points give: Restricted and Suspended from their thresholds (BR-BOR-6).</summary>
    public StaffMemberStatus GetStatus(int activePoints)
    {
        if (activePoints < rules.RestrictedFromPoints)
        {
            return StaffMemberStatus.Active;
        }

        if (activePoints < rules.SuspendedFromPoints)
        {
            return StaffMemberStatus.Restricted;
        }

        return StaffMemberStatus.Suspended;
    }
}
