using LibraryApp.Application.Borrowing.Options;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Tests.Borrowing.Fakes;

/// <summary>
/// Borrowing settings for unit tests: the values of the "Borrowing" section in appsettings.json, changed per test when a
/// test checks another value.
/// </summary>
public static class BorrowingTestSettings
{
    public static BorrowingOptions Defaults() => new()
    {
        Penalties = new PenaltyOptions
        {
            PointsPerLateWorkingDay = 1,
            MaxLatePoints = 10,
            DamagedPoints = 5,
            LostPoints = 10,
            LifetimeDays = 90,
            RestrictedFromPoints = 5,
            SuspendedFromPoints = 10,
        },
        Reservations = new ReservationOptions
        {
            MaxActive = 3,
            PickupWorkingDays = 3,
        },
    };

    public static IOptions<BorrowingOptions> Create(Action<BorrowingOptions>? change = null)
    {
        BorrowingOptions options = Defaults();
        change?.Invoke(options);
        return Options.Create(options);
    }
}
