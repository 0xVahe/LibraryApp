using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests;

/// <summary>The calendar of the real host reads the Armenian public holidays from appsettings.json (BR-BOR-5).</summary>
public class WorkingDayCalendarSettingsTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Fact]
    public void The_settings_list_the_Armenian_public_holidays_of_2026()
    {
        DateOnly[] expected =
        [
            new(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 6), new(2026, 1, 27), new(2026, 1, 28), new(2026, 3, 8),
            new(2026, 4, 24), new(2026, 5, 1), new(2026, 5, 9), new(2026, 5, 28), new(2026, 7, 5), new(2026, 9, 21), new(2026, 12, 31),
        ];

        Assert.Equal(expected, factory.Services.GetRequiredService<IOptions<HolidayOptions>>().Value.Holidays);
    }

    [Fact]
    public void The_registered_calendar_skips_a_configured_holiday()
    {
        var calendar = factory.Services.GetRequiredService<IWorkingDayCalendar>();

        // Independence Day, Monday 21 September 2026.
        Assert.False(calendar.IsWorkingDay(new DateOnly(2026, 9, 21)));
        Assert.Equal(new DateOnly(2026, 9, 22), calendar.AddWorkingDays(new DateOnly(2026, 9, 18), 1));
    }
}
