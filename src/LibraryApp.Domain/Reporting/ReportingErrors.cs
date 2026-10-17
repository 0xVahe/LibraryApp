using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Reporting;

public static class ReportingErrors
{
    public static readonly Error InvalidOverdueFilter =
        Error.Validation(
            "reporting.invalid_overdue_filter",
            "Overdue day filters must be non-negative, and the minimum cannot exceed the maximum.");

    public static readonly Error InvalidDepartmentId =
        Error.Validation(
            "reporting.invalid_department_id",
            "Department ID cannot be empty.");

    public static readonly Error InvalidDateRange =
        Error.Validation(
            "reporting.invalid_date_range",
            "A valid date range is required, with the start date on or before the end date.");
}
