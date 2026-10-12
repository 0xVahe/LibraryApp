using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Common;

/// <summary>Errors raised by the platform (unit of work, locks), not by a module rule: no module prefix in the code.</summary>
public static class CommonErrors
{
    public static readonly Error ValidationFailed =
        Error.Validation("validation_failed", "One or more validation errors occurred.");

    public static readonly Error ConcurrencyConflict =
        Error.Conflict("concurrency_conflict", "The resource was modified by another request. Reload and retry.");

    public static readonly Error UniqueViolation =
        Error.Conflict("unique_violation", "The operation conflicts with the current state of the resource.");

    public static readonly Error ReferenceInUse =
        Error.Conflict("reference_in_use", "The item is still referenced by other data and cannot be deleted.");

    public static readonly Error ReferenceMissing =
        Error.Conflict("reference_missing", "A referenced item no longer exists. Reload and retry.");

    public static readonly Error ResourceBusy =
        Error.Conflict("resource_busy", "The resource is being modified by another request. Retry shortly.");
}
