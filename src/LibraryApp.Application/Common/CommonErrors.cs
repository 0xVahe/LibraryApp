using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Common;

public static class CommonErrors
{
    public static readonly Error ValidationFailed =
        Error.Validation("validation_failed", "One or more validation errors occurred.");

    public static readonly Error ConcurrencyConflict =
        Error.Conflict("concurrency_conflict", "The resource was modified by another request. Reload and retry.");

    public static readonly Error UniqueViolation =
        Error.Conflict("unique_violation", "The operation conflicts with the current state of the resource.");

    public static readonly Error ResourceBusy =
        Error.Conflict("resource_busy", "The resource is being modified by another request. Retry shortly.");
}
