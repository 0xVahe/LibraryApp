using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LibraryApp.Infrastructure.IntegrationTests.Borrowing;

/// <summary>AddBorrowingModule binds the "Borrowing" section, checks it and builds the penalty policy from it (SD-21).</summary>
public class BorrowingRegistrationTests
{
    private static readonly Dictionary<string, string?> Valid = new()
    {
        ["Borrowing:Penalties:PointsPerLateWorkingDay"] = "2",
        ["Borrowing:Penalties:MaxLatePoints"] = "8",
        ["Borrowing:Penalties:DamagedPoints"] = "4",
        ["Borrowing:Penalties:LostPoints"] = "9",
        ["Borrowing:Penalties:LifetimeDays"] = "60",
        ["Borrowing:Penalties:RestrictedFromPoints"] = "4",
        ["Borrowing:Penalties:SuspendedFromPoints"] = "8",
        ["Borrowing:Penalties:SuspensionDays"] = "21",
        ["Borrowing:Penalties:LateAndDamaged"] = "Highest",
        ["Borrowing:Loans:OverdueFrom"] = "DueTime",
        ["Borrowing:Reservations:MaxActive"] = "2",
        ["Borrowing:Reservations:PickupWorkingDays"] = "5",
        ["Borrowing:Reservations:PickupCountsReleaseDay"] = "true",
        ["Borrowing:Penalties:AfterSuspension"] = "RestrictedByPoints",
    };

    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBorrowingModule(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_section_is_bound()
    {
        using var services = Services(Valid);

        BorrowingOptions options = services.GetRequiredService<IOptions<BorrowingOptions>>().Value;
        Assert.Equal(60, options.Penalties.LifetimeDays);
        Assert.Equal(2, options.Reservations.MaxActive);
        Assert.Equal(5, options.Reservations.PickupWorkingDays);
        Assert.True(options.Reservations.PickupCountsReleaseDay);
        Assert.False(options.Reservations.RestrictedMayReserve);
        Assert.Equal(21, options.Penalties.SuspensionDays);
        Assert.Equal(LateAndDamagedMode.Highest, options.Penalties.LateAndDamaged);
        Assert.Equal(OverdueFromMode.DueTime, options.Loans.OverdueFrom);
        Assert.Equal(
            AfterSuspensionMode.RestrictedByPoints,
            options.Penalties.AfterSuspension);
    }

    [Fact]
    public void The_penalty_policy_uses_the_configured_values()
    {
        using var services = Services(Valid);

        var policy = services.GetRequiredService<PenaltyPolicy>();
        Assert.Equal(6, policy.CalculateLatePoints(3));
        Assert.Equal(8, policy.CalculateLatePoints(10));
        Assert.Equal(4, policy.CalculateDamagedPoints());
        Assert.Equal(9, policy.CalculateLostPoints());
        Assert.Equal(StaffMemberStatus.Restricted, policy.GetStatus(4));
        Assert.Equal(StaffMemberStatus.Suspended, policy.GetStatus(8));
    }

    [Fact]
    public void A_missing_section_fails()
    {
        using var services = Services(new());

        var ex = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<BorrowingOptions>>().Value);
        Assert.Contains("Borrowing:Penalties:LifetimeDays", ex.Message);
    }

    [Theory]
    [InlineData("Borrowing:Penalties:RestrictedFromPoints", "8")]
    [InlineData("Borrowing:Penalties:LostPoints", "11")]
    [InlineData("Borrowing:Reservations:PickupWorkingDays", "0")]
    public void Invalid_settings_fail_validation(string key, string value)
    {
        using var services = Services(new(Valid) { [key] = value });

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<BorrowingOptions>>().Value);
    }
}
