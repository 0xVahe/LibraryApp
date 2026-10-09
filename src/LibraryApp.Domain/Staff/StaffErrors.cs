using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Staff;

/// <summary>Errors of the Staff module. Codes never change; messages may be reworded.</summary>
public static class StaffErrors
{
    // Enrollment and profile management (MEM-01, MEM-03, BR-MEM-1).
    public static readonly Error EnrolEmailNotCorporate =
        Error.Validation("staff.enrol_email_not_corporate", "Only Ameriabank staff can be enrolled");
    public static readonly Error EmployeeIdTaken =
        Error.Conflict("staff.employee_id_taken", "Employee ID is already enrolled.");
    public static readonly Error EmailTaken =
        Error.Conflict("staff.email_taken", "Corporate email is already in use.");
    public static readonly Error DepartmentNotFound =
        Error.Validation("staff.department_not_found", "Department was not found.");
    public static readonly Error CategoryNotFound =
        Error.Validation("staff.category_not_found", "Staff category was not found.");
    public static readonly Error RoleAssignmentForbidden =
        Error.Forbidden("staff.role_assignment_forbidden", "Only a Library Administrator may assign a privileged role.");
    public static readonly Error AccountWriteFailed =
        Error.Validation("staff.account_write_failed", "The user account could not be saved.");
    public static readonly Error AccountRoleInvalid =
        Error.Conflict("staff.account_role_invalid", "The staff account must have exactly one supported role.");
    public static readonly Error CategoryNameTaken =
        Error.Conflict("staff.category_name_taken", "Staff category name is already in use.");
    
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
    
    public static readonly Error CategoryResourceNotFound =
        Error.NotFound("staff.category_resource_not_found", "Staff category was not found.");
    
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

    public static readonly Error DeactivatedProfileLocked =
        Error.Conflict("staff.deactivated_profile_locked", "A deactivated staff member's profile cannot be updated.");

    public static readonly Error DeactivatedCannotChangeStatus =
        Error.BusinessRule("staff.deactivated_status_locked",
            "The status of a deactivated staff member cannot be changed.");

    // Lookup and eligibility
    public static readonly Error NotFound =
        Error.NotFound("staff.not_found", "The staff member was not found.");

    public static readonly Error Suspended =
        Error.BusinessRule("staff.suspended", "The staff member is suspended and cannot borrow.");

    public static readonly Error Deactivated =
        Error.BusinessRule("staff.deactivated", "The staff member is deactivated and cannot borrow.");

    /// <summary>Used by Borrowing, which owns the loan count, so the message is the same everywhere.</summary>
    public static Error LoanLimitReached(int activeLoans, int maxLoans) =>
        Error.BusinessRule("staff.loan_limit_reached", $"Loan limit reached ({activeLoans}/{maxLoans}).");

    // Login (MEM-02, MEM-03)
    public static readonly Error EmailNotCorporate =
        Error.Validation("staff.email_not_corporate", "Only Ameriabank staff can log in.");

    /// <summary>
    /// Same message for unknown email and wrong password, so accounts cannot be guessed. Never thrown:
    /// 401 is not an ErrorKind, AuthController answers it with this code.
    /// </summary>
    public static readonly Error InvalidCredentials =
        Error.Validation("staff.invalid_credentials", "Invalid email or password.");

    public static readonly Error AccountDeactivated =
        Error.Forbidden("staff.account_deactivated", "This account is deactivated.");
}
