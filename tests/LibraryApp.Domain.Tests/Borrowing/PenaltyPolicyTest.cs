using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Domain.Tests.Borrowing;

public class PenaltyPolicyTests
{
    // The values of the "Borrowing:Penalties" section in appsettings.json.
    private static readonly PenaltyRules DefaultRules = new(
        PointsPerLateWorkingDay: 1, MaxLatePoints: 10, DamagedPoints: 5, LostPoints: 10,
        LifetimeDays: 90, RestrictedFromPoints: 5, SuspendedFromPoints: 10);

    private readonly PenaltyPolicy _policy = new(DefaultRules);

    [Fact]
    public void FourPoints_ShouldBeActive()
    {
        var result = _policy.GetStatus(4);

        Assert.Equal(StaffMemberStatus.Active, result);
    }

    [Fact]
    public void FivePoints_ShouldBeRestricted()
    {
        var result = _policy.GetStatus(5);

        Assert.Equal(StaffMemberStatus.Restricted, result);
    }

    [Fact]
    public void NinePoints_ShouldBeRestricted()
    {
        var result = _policy.GetStatus(9);

        Assert.Equal(StaffMemberStatus.Restricted, result);
    }

    [Fact]
    public void TenPoints_ShouldBeSuspended()
    {
        var result = _policy.GetStatus(10);

        Assert.Equal(StaffMemberStatus.Suspended, result);
    }

    [Fact]
    public void MoreThanTenLateDays_ShouldBeCappedAtTenPoints()
    {
        var result = _policy.CalculateLatePoints(15);

        Assert.Equal(10, result);
    }

    [Fact]
    public void LostBook_ShouldGiveTenPoints()
    {
        var result = _policy.CalculateLostPoints();

        Assert.Equal(10, result);
    }

    [Fact]
    public void DamagedBook_ShouldGiveFivePoints()
    {
        var result = _policy.CalculateDamagedPoints();

        Assert.Equal(5, result);
    }

    [Fact]
    public void PenaltyRecord_ShouldExpireAfterTheLifetime()
    {
        var createdAtUtc = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        Assert.Equal(createdAtUtc.AddDays(90), _policy.ExpiresAtUtc(createdAtUtc));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    [InlineData(2, 4)]
    [InlineData(3, 6)]
    [InlineData(4, 6)]
    [InlineData(int.MaxValue, 6)]
    public void LatePoints_FollowTheConfiguredRateAndCap(int lateWorkingDays, int expected)
    {
        var policy = new PenaltyPolicy(DefaultRules with { PointsPerLateWorkingDay = 2, MaxLatePoints = 6 });

        Assert.Equal(expected, policy.CalculateLatePoints(lateWorkingDays));
    }

    [Theory]
    [InlineData(2, StaffMemberStatus.Active)]
    [InlineData(3, StaffMemberStatus.Restricted)]
    [InlineData(6, StaffMemberStatus.Restricted)]
    [InlineData(7, StaffMemberStatus.Suspended)]
    public void Status_FollowsTheConfiguredThresholds(int activePoints, StaffMemberStatus expected)
    {
        var policy = new PenaltyPolicy(DefaultRules with { RestrictedFromPoints = 3, SuspendedFromPoints = 7 });

        Assert.Equal(expected, policy.GetStatus(activePoints));
    }

    [Fact]
    public void ConfiguredValues_AreUsedForDamagedLostAndLifetime()
    {
        var policy = new PenaltyPolicy(DefaultRules with { DamagedPoints = 3, LostPoints = 8, LifetimeDays = 30 });
        var createdAtUtc = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        Assert.Equal(3, policy.CalculateDamagedPoints());
        Assert.Equal(8, policy.CalculateLostPoints());
        Assert.Equal(createdAtUtc.AddDays(30), policy.ExpiresAtUtc(createdAtUtc));
    }
}