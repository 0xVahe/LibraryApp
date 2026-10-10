namespace LibraryApp.Domain.Common;

/// <summary>
/// Thrown when a rule is broken. Carries an <see cref="Common.Error"/>; the API layer turns it into a ProblemDetails
/// response with the status that matches <see cref="Error.Kind"/>.
/// </summary>
public sealed class DomainException(
    Error error,
    Exception? innerException = null,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(error.Message, innerException)
{
    public Error Error { get; } = error;

    /// <summary>Structured rule details for the API; never includes exception internals.</summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();
}
