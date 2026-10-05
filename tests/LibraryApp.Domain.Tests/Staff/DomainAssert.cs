using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Tests.Staff;

internal static class DomainAssert
{
    /// <summary>Checks that the action breaks a rule and that the rule is the expected one.</summary>
    public static void ThrowsError(Error expected, Action action)
    {
        var exception = Assert.Throws<DomainException>(action);

        Assert.Equal(expected, exception.Error);
    }
}