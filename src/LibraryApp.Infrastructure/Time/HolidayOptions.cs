using LibraryApp.Application.Abstractions.Common;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.Time;

/// <summary>Configuration section <c>WorkingDayCalendar</c>: the Armenian public holidays (BR-BOR-5). Updated every year.</summary>
public sealed class HolidayOptions
{
    public const string SectionName = "WorkingDayCalendar";

    public List<DateOnly> Holidays { get; set; } = [];
}

/// <summary>
/// Fails at start when the list has no holiday in the current library year: a forgotten yearly update would otherwise
/// count public holidays as working days and give wrong penalty points and deadlines without any error.
/// </summary>
public sealed class HolidayOptionsValidator(IClock clock) : IValidateOptions<HolidayOptions>
{
    public ValidateOptionsResult Validate(string? name, HolidayOptions options)
    {
        int year = clock.LocalToday.Year;
        return options.Holidays.Any(date => date.Year == year)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{HolidayOptions.SectionName}:{nameof(HolidayOptions.Holidays)} has no date in {year}. Add the Armenian public holidays of {year}.");
    }
}
