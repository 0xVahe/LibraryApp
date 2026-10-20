namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>
/// Represents the payload required to waive an active borrowing penalty.
/// </summary>
public sealed class WaivePenaltyRequest
{
    /// <summary>
    /// Gets or sets the mandatory explanation for waiving the penalty.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}