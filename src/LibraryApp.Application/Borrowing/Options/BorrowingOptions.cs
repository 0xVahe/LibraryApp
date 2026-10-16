using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Options;

/// <summary>
/// Settings section <c>Borrowing</c>: the values and decided variants of the Borrowing rules (SD-21). The values live in
/// <c>appsettings.json</c>, not in code; <see cref="BorrowingOptionsValidator"/> stops the application at start when one
/// is missing or inconsistent.
/// </summary>
public sealed class BorrowingOptions
{
    public const string SectionName = "Borrowing";

    public PenaltyOptions Penalties { get; set; } = new();

    public ReservationOptions Reservations { get; set; } = new();
}

/// <summary>Section <c>Borrowing:Penalties</c> — the penalty-point policy (BOR-05, BR-BOR-6).</summary>
public sealed class PenaltyOptions
{
    /// <summary>Points for each working day a loan is returned late (BOR-05).</summary>
    public int PointsPerLateWorkingDay { get; set; }

    /// <summary>Cap on the late points of one return (BOR-05).</summary>
    public int MaxLatePoints { get; set; }

    /// <summary>Points for a copy returned damaged (BOR-05, BOR-09).</summary>
    public int DamagedPoints { get; set; }

    /// <summary>Points for a lost copy (BOR-05, BOR-09).</summary>
    public int LostPoints { get; set; }

    /// <summary>Calendar days a penalty record counts (BR-BOR-6).</summary>
    public int LifetimeDays { get; set; }

    /// <summary>Active points from which the employee is Restricted (BR-BOR-6).</summary>
    public int RestrictedFromPoints { get; set; }

    /// <summary>Active points from which the employee is Suspended (BR-BOR-6).</summary>
    public int SuspendedFromPoints { get; set; }

    /// <summary>The same values for the Domain policy, which does not read configuration.</summary>
    public PenaltyRules ToRules() => new(
        PointsPerLateWorkingDay,
        MaxLatePoints,
        DamagedPoints,
        LostPoints,
        LifetimeDays,
        RestrictedFromPoints,
        SuspendedFromPoints);
}

/// <summary>Section <c>Borrowing:Reservations</c> — the reservation queue (BOR-07, BR-BOR-2).</summary>
public sealed class ReservationOptions
{
    /// <summary>Active (Waiting or ReadyForPickup) reservations one employee may hold (BR-BOR-2).</summary>
    public int MaxActive { get; set; }

    /// <summary>Working days a copy is held for the first person in the queue (BOR-07).</summary>
    public int PickupWorkingDays { get; set; }

    /// <summary>
    /// Whether the day the copy is held counts as the first pickup day when it is a working day (BQ-BA-12). False:
    /// held on Friday with 3 days → until the end of Wednesday; true → until the end of Tuesday.
    /// </summary>
    public bool PickupCountsReleaseDay { get; set; }

    /// <summary>Whether a Restricted employee may place new reservations (BR-MEM-3, BQ-BA-10).</summary>
    public bool RestrictedMayReserve { get; set; }

    /// <summary>Whether a Suspended employee may place new reservations (BQ-BA-11); a Deactivated one never may.</summary>
    public bool SuspendedMayReserve { get; set; }
}
