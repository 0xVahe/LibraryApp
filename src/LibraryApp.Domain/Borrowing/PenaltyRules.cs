namespace LibraryApp.Domain.Borrowing;

/// <summary>
/// The values of the penalty-point policy (BOR-05, BR-BOR-6). Plain values only: the Domain does not read configuration;
/// the Borrowing module builds them from the "Borrowing:Penalties" settings and validates them at start.
/// </summary>
/// <param name="PointsPerLateWorkingDay">Points for each working day a loan is returned late.</param>
/// <param name="MaxLatePoints">Cap on the late points of one return.</param>
/// <param name="DamagedPoints">Points for a copy returned damaged.</param>
/// <param name="LostPoints">Points for a lost copy.</param>
/// <param name="LifetimeDays">Calendar days a penalty record counts after it is created.</param>
/// <param name="RestrictedFromPoints">Active points from which the employee is Restricted.</param>
/// <param name="SuspendedFromPoints">Active points from which the employee is Suspended.</param>
public sealed record PenaltyRules(
    int PointsPerLateWorkingDay,
    int MaxLatePoints,
    int DamagedPoints,
    int LostPoints,
    int LifetimeDays,
    int RestrictedFromPoints,
    int SuspendedFromPoints);
