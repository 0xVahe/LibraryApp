using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Staff;

/// <summary>
/// An Ameriabank employee enrolled in the library: the only kind of borrower (MEM-01).
/// Other modules refer to it by <see cref="Entity.Id"/> only.
/// </summary>
public sealed class StaffMember : Entity
{
    private StaffMember() { } // for EF Core only

    /// <param name="id">From <c>IIdGenerator</c>.</param>
    /// <param name="userId">The login account of this employee (one account per staff member).</param>
    /// <param name="joinedLibraryAtUtc">From <c>IClock.UtcNow</c>.</param>
    public StaffMember(
        Guid id,
        Guid userId,
        string employeeId,
        string firstName,
        string lastName,
        string corporateEmail,
        Guid departmentId,
        string position,
        string? lineManagerEmail,
        Guid staffCategoryId,
        DateTime joinedLibraryAtUtc) : base(id)
    {
        UserId = StaffGuard.RequireId(userId, StaffErrors.UserRequired);
        EmployeeId = StaffGuard.RequireText(employeeId, StaffErrors.EmployeeIdRequired);

        UpdateProfile(firstName, lastName, corporateEmail, position, lineManagerEmail);
        MoveToDepartment(departmentId);
        ChangeCategory(staffCategoryId);

        Status = StaffMemberStatus.Active;
        JoinedLibraryAtUtc = joinedLibraryAtUtc;
    }

    /// <summary>Id of the login account (Identity). Unique: one library account per employee (BR-MEM-1).</summary>
    public Guid UserId { get; private set; }

    /// <summary>The bank's employee number. Unique (BR-MEM-1) and never changes after enrolment.</summary>
    public string EmployeeId { get; private set; } = null!;

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    /// <summary>Stored in lower case. The corporate-domain rule (MEM-03) is checked by the service, not here.</summary>
    public string CorporateEmail { get; private set; } = null!;

    public Guid DepartmentId { get; private set; }

    public string Position { get; private set; } = null!;

    /// <summary>Copied on overdue and suspension notices. Optional.</summary>
    public string? LineManagerEmail { get; private set; }

    public Guid StaffCategoryId { get; private set; }

    public StaffMemberStatus Status { get; private set; }

    public DateTime JoinedLibraryAtUtc { get; private set; }

    public void UpdateProfile(
        string firstName,
        string lastName,
        string corporateEmail,
        string position,
        string? lineManagerEmail)
    {
        FirstName = StaffGuard.RequireText(firstName, StaffErrors.FirstNameRequired);
        LastName = StaffGuard.RequireText(lastName, StaffErrors.LastNameRequired);
        CorporateEmail = StaffGuard.RequireText(corporateEmail, StaffErrors.CorporateEmailRequired).ToLowerInvariant();
        Position = StaffGuard.RequireText(position, StaffErrors.PositionRequired);
        LineManagerEmail = string.IsNullOrWhiteSpace(lineManagerEmail)
            ? null
            : lineManagerEmail.Trim().ToLowerInvariant();
    }

    public void MoveToDepartment(Guid departmentId) =>
        DepartmentId = StaffGuard.RequireId(departmentId, StaffErrors.DepartmentRequired);

    public void ChangeCategory(Guid staffCategoryId) =>
        StaffCategoryId = StaffGuard.RequireId(staffCategoryId, StaffErrors.StaffCategoryRequired);

    /// <summary>
    /// Sets Active, Restricted or Suspended (called by Borrowing through <c>IStaffMemberService.SetStatusAsync</c>).
    /// Use <see cref="Deactivate"/> for an employee who left the bank.
    /// </summary>
    public void ChangeStatus(StaffMemberStatus status)
    {
        if (Status == StaffMemberStatus.Deactivated)
            throw new DomainException(StaffErrors.DeactivatedCannotChangeStatus);

        if (status is not (StaffMemberStatus.Active or StaffMemberStatus.Restricted or StaffMemberStatus.Suspended))
            throw new DomainException(StaffErrors.StatusInvalid);

        Status = status;
    }

    /// <summary>
    /// The employee left the bank. The check "no active loans" (BR-MEM-4) needs Borrowing data,
    /// so the service makes it before calling this method.
    /// </summary>
    public void Deactivate()
    {
        if (Status == StaffMemberStatus.Deactivated)
            throw new DomainException(StaffErrors.AlreadyDeactivated);

        Status = StaffMemberStatus.Deactivated;
    }
}