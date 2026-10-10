using LibraryApp.Domain.Staff;

namespace LibraryApp.Domain.Borrowing;

public class PenaltyPolicy
{
    public const int PointsPerLateWorkingDay = 1;
    public const int MaxLatePoints = 10;

    public const int LostPoints = 10;
    public const int DamagedPoints = 5;

    public const int RestrictedFromPoints = 5;
    public const int SuspendedFromPoints = 10;

    public int CalculateLatePoints(int lateWorkingDays)
    {
        if (lateWorkingDays <= 0)
        {
            return 0;
        }

        return Math.Min(
            lateWorkingDays * PointsPerLateWorkingDay,
            MaxLatePoints);
    }

    public int CalculateLostPoints()
    {
        return LostPoints;
    }

    public int CalculateDamagedPoints()
    {
        return DamagedPoints;
    }

    /// <summary>The status the active penalty points give: 5 or more Restricted, 10 or more Suspended (BR-BOR-6).</summary>
    public StaffMemberStatus GetStatus(int activePoints)
    {
        if (activePoints < RestrictedFromPoints)
        {
            return StaffMemberStatus.Active;
        }

        if (activePoints < SuspendedFromPoints)
        {
            return StaffMemberStatus.Restricted;
        }

        return StaffMemberStatus.Suspended;
    }
}