namespace LibraryApp.Application.Borrowing.Options;

/// <summary>
/// Determines staff standing after the suspension period ends.
/// </summary>
public enum AfterSuspensionMode
{
    /// <summary>
    /// Restricted while points meet the Restricted threshold;
    /// otherwise Active. Existing high points do not restart suspension.
    /// </summary>
    RestrictedByPoints = 1,

    /// <summary>
    /// Active when the suspension period ends, regardless of
    /// the remaining points.
    /// </summary>
    Active = 2
}