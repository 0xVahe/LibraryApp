namespace LibraryApp.Domain.Borrowing;

/// <summary>
/// Represents the lifecycle state of a penalty record.
/// </summary>
public enum PenaltyRecordStatus
{
    /// <summary>
    /// The penalty has not been waived or marked expired.
    /// Its expiry timestamp must also be checked when counting points.
    /// </summary>
    Active = 1,

    /// <summary>
    /// The penalty's validity period has ended.
    /// </summary>
    Expired = 2,

    /// <summary>
    /// The penalty was waived with a recorded explanation.
    /// </summary>
    Waived = 3
}