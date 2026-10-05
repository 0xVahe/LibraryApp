using LibraryApp.Domain.Staff;

namespace LibraryApp.Domain.Tests.Staff;

public class StaffMemberStatusTests
{
    // The numbers are stored in the database: renumbering would change the meaning of existing rows.
    [Theory]
    [InlineData(StaffMemberStatus.Active, 1)]
    [InlineData(StaffMemberStatus.Restricted, 2)]
    [InlineData(StaffMemberStatus.Suspended, 3)]
    [InlineData(StaffMemberStatus.Deactivated, 4)]
    public void Values_are_stable(StaffMemberStatus status, int expected)
    {
        Assert.Equal(expected, (int)status);
    }
}