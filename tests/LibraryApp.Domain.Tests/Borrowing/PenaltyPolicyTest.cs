using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Domain.Tests.Borrowing;

public class PenaltyPolicyTests
{
    private readonly PenaltyPolicy _policy = new();

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
}