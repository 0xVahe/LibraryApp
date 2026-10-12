namespace LibraryApp.Domain.Common;

/// <summary>
/// What kind of problem an <see cref="Error"/> describes. The API layer maps it to an HTTP status (SD-05),
/// so a module never has to know about HTTP.
/// </summary>
public enum ErrorKind
{
    /// <summary>A business rule forbids the operation (HTTP 422). Example: "Maximum renewals reached".</summary>
    BusinessRule = 1,

    /// <summary>The input is invalid (HTTP 400).</summary>
    Validation = 2,

    /// <summary>The caller may not do this (HTTP 403).</summary>
    Forbidden = 3,

    /// <summary>The thing does not exist (HTTP 404).</summary>
    NotFound = 4,

    /// <summary>The operation clashes with the current state (HTTP 409). Example: duplicate ISBN.</summary>
    Conflict = 5,

    /// <summary>The caller must authenticate (HTTP 401).</summary>
    Unauthorized = 6,
}

/// <summary>
/// An error with a stable machine-readable code and a human-readable message, defined once (SD-04, option B).
/// Code format: <c>&lt;module&gt;.&lt;what_happened&gt;</c> in lower snake case, for example <c>borrowing.renew_max</c>.
/// The code never changes; the message may be reworded.
/// </summary>
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.BusinessRule)
{
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorKind.BusinessRule);
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);
}
