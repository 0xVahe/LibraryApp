using LibraryApp.Infrastructure.Persistence.Converters;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

public class UtcDateTimeConverterTests
{
    private readonly UtcDateTimeConverter _converter = new();

    [Fact]
    public void A_UTC_value_is_written_unchanged()
    {
        var utc = new DateTime(2026, 10, 12, 20, 30, 0, DateTimeKind.Utc);

        Assert.Equal(utc, _converter.ConvertToProvider(utc));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void A_non_UTC_value_is_refused(DateTimeKind kind)
    {
        var value = new DateTime(2026, 10, 12, 20, 30, 0, kind);

        var ex = Assert.Throws<InvalidOperationException>(() => _converter.ConvertToProvider(value));
        Assert.Contains(kind.ToString(), ex.Message);
    }

    [Fact]
    public void A_value_read_from_the_database_is_marked_as_UTC()
    {
        var stored = new DateTime(2026, 10, 12, 20, 30, 0, DateTimeKind.Unspecified);

        var read = (DateTime)_converter.ConvertFromProvider(stored)!;

        Assert.Equal(DateTimeKind.Utc, read.Kind);
        Assert.Equal(stored.Ticks, read.Ticks);
    }
}
