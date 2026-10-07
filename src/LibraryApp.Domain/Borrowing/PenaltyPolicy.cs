namespace LibraryApp.Domain.Borrowing;

public class PenaltyPolicy
{
    public const int PointsPerLateWorkingDay = 1;
    public const int MaxLatePoints = 10;

    public const int LostPoints = 10;
    public const int DamagedPoints = 5;

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

    public string GetStatus(int activePoints)
    {
        if (activePoints < 5)
        {
            return "Active";
        }

        if (activePoints < 10)
        {
            return "Restricted";
        }

        return "Suspended";
    }
}