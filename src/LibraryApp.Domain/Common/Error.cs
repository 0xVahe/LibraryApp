namespace LibraryApp.Domain.Common;

public enum ErrorKind
{
    BusinessRule = 1,
    Validation = 2,
    Forbidden = 3,
    NotFound = 4,
    Conflict = 5,
}

public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.BusinessRule)
{
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorKind.BusinessRule);
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
}
