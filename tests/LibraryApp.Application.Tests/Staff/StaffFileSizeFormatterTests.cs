using System.Globalization;
using LibraryApp.Application.Staff.Formatting;

namespace LibraryApp.Application.Tests.Staff;

public sealed class StaffFileSizeFormatterTests
{
    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(999L, "999 bytes")]
    [InlineData(1000L, "1 KB")]
    [InlineData(2048L, "2.05 KB")]
    [InlineData(500000L, "500 KB")]
    [InlineData(1000000L, "1 MB")]
    [InlineData(1500000L, "1.5 MB")]
    public void Formats_decimal_sizes(long bytes, string expected) =>
        Assert.Equal(expected, StaffFileSizeFormatter.Format(bytes));

    [Fact]
    public void Formatting_does_not_depend_on_the_server_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("1.5 MB", StaffFileSizeFormatter.Format(1500000));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
