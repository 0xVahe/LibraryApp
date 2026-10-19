using LibraryApp.Application.Borrowing.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>The real host reads the Borrowing rule values from appsettings.json (SD-21) and they pass the start check.</summary>
public class BorrowingSettingsTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    [Fact]
    public void The_settings_hold_the_agreed_rule_values()
    {
        BorrowingOptions options = factory.Services.GetRequiredService<IOptions<BorrowingOptions>>().Value;

        Assert.Equal(
            (1, 10, 5, 10, 90, 5, 10),
            (options.Penalties.PointsPerLateWorkingDay, options.Penalties.MaxLatePoints, options.Penalties.DamagedPoints,
                options.Penalties.LostPoints, options.Penalties.LifetimeDays, options.Penalties.RestrictedFromPoints,
                options.Penalties.SuspendedFromPoints));
        Assert.Equal((3, 3), (options.Reservations.MaxActive, options.Reservations.PickupWorkingDays));
        Assert.False(options.Reservations.PickupCountsReleaseDay);
        Assert.False(options.Reservations.RestrictedMayReserve);
        Assert.False(options.Reservations.SuspendedMayReserve);
        Assert.True(options.Reservations.AnyCopyFulfils);
        Assert.True(options.Loans.AllowLostBeforeDue);
    }
}
