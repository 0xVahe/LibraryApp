using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Staff;

/// <summary>Errors of the Staff module. Codes never change; messages may be reworded.</summary>
public static class StaffErrors
{
    // Department
    public static readonly Error DepartmentNameRequired =
        Error.Validation("staff.department_name_required", "Department name is required.");

    // Staff category
    public static readonly Error CategoryNameRequired =
        Error.Validation("staff.category_name_required", "Staff category name is required.");

    public static readonly Error MaxLoansInvalid =
        Error.Validation("staff.max_loans_invalid", "Maximum loans must be greater than zero.");

    public static readonly Error LoanDaysInvalid =
        Error.Validation("staff.loan_days_invalid", "Loan days must be greater than zero.");

    public static readonly Error MaxRenewalsInvalid =
        Error.Validation("staff.max_renewals_invalid", "Maximum renewals must not be negative.");

    // Staff member
    public static readonly Error EmployeeIdRequired =
        Error.Validation("staff.employee_id_required", "Employee ID is required.");

    public static readonly Error FirstNameRequired =
        Error.Validation("staff.first_name_required", "First name is required.");

    public static readonly Error LastNameRequired =
        Error.Validation("staff.last_name_required", "Last name is required.");

    public static readonly Error CorporateEmailRequired =
        Error.Validation("staff.corporate_email_required", "Corporate email is required.");

    public static readonly Error PositionRequired =
        Error.Validation("staff.position_required", "Position is required.");

    public static readonly Error DepartmentRequired =
        Error.Validation("staff.department_required", "Department is required.");

    public static readonly Error StaffCategoryRequired =
        Error.Validation("staff.staff_category_required", "Staff category is required.");

    public static readonly Error UserRequired =
        Error.Validation("staff.user_required", "A user account is required.");

    public static readonly Error StatusInvalid =
        Error.Validation("staff.status_invalid", "The staff member status is not valid.");

    public static readonly Error AlreadyDeactivated =
        Error.BusinessRule("staff.already_deactivated", "The staff member is already deactivated.");

    public static readonly Error DeactivatedCannotChangeStatus =
        Error.BusinessRule("staff.deactivated_status_locked", "The status of a deactivated staff member cannot be changed.");
}