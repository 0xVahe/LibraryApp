using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Tests.Borrowing.Fakes;

namespace LibraryApp.Application.Tests.Borrowing;

/// <summary>The "Borrowing" settings are checked at start (SD-21): a missing or inconsistent value stops the application.</summary>
public sealed class BorrowingOptionsValidatorTests
{
    private static readonly BorrowingOptionsValidator Validator = new();

    [Fact]
    public void The_values_of_appsettings_are_valid()
    {
        Assert.True(Validator.Validate(null, BorrowingTestSettings.Defaults()).Succeeded);
    }

    [Fact]
    public void A_missing_section_fails_and_names_every_key()
    {
        var result = Validator.Validate(null, new BorrowingOptions());

        Assert.True(result.Failed);
        foreach (string key in new[]
                 {
                     "Borrowing:Penalties:PointsPerLateWorkingDay", "Borrowing:Penalties:MaxLatePoints",
                     "Borrowing:Penalties:DamagedPoints", "Borrowing:Penalties:LostPoints", "Borrowing:Penalties:LifetimeDays",
                     "Borrowing:Penalties:RestrictedFromPoints", "Borrowing:Penalties:SuspendedFromPoints",
                     "Borrowing:Reservations:MaxActive", "Borrowing:Reservations:PickupWorkingDays",
                 })
            Assert.Contains(key, result.FailureMessage);
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(12, 10)]
    public void Restricted_must_start_below_Suspended(int restrictedFrom, int suspendedFrom)
    {
        var options = BorrowingTestSettings.Defaults();
        options.Penalties.RestrictedFromPoints = restrictedFrom;
        options.Penalties.SuspendedFromPoints = suspendedFrom;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("RestrictedFromPoints", result.FailureMessage);
    }

    [Fact]
    public void The_late_cap_must_not_be_lower_than_one_day()
    {
        var options = BorrowingTestSettings.Defaults();
        options.Penalties.PointsPerLateWorkingDay = 4;
        options.Penalties.MaxLatePoints = 3;

        Assert.True(Validator.Validate(null, options).Failed);
    }

    [Theory]
    [InlineData(nameof(PenaltyOptions.LostPoints))]
    [InlineData(nameof(PenaltyOptions.DamagedPoints))]
    [InlineData(nameof(PenaltyOptions.MaxLatePoints))]
    public void One_record_cannot_hold_more_than_the_schema_allows(string key)
    {
        var options = BorrowingTestSettings.Defaults();
        typeof(PenaltyOptions).GetProperty(key)!.SetValue(options.Penalties, 11);

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"Borrowing:Penalties:{key} must be between 1 and 10", result.FailureMessage);
    }

    [Fact]
    public void Other_values_are_accepted()
    {
        var options = BorrowingTestSettings.Defaults();
        options.Penalties.PointsPerLateWorkingDay = 2;
        options.Penalties.LifetimeDays = 180;
        options.Penalties.RestrictedFromPoints = 6;
        options.Penalties.SuspendedFromPoints = 12;
        options.Reservations.MaxActive = 5;
        options.Reservations.PickupWorkingDays = 1;

        Assert.True(Validator.Validate(null, options).Succeeded);
    }
}
