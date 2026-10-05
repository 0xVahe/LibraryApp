using LibraryApp.Domain.Staff;

namespace LibraryApp.Domain.Tests.Staff;

public class StaffMemberTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid DepartmentId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly DateTime JoinedAt = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);

    private static StaffMember Create(
        Guid? id = null,
        Guid? userId = null,
        string employeeId = "EMP-0001",
        string firstName = "Anna",
        string lastName = "Petrosyan",
        string corporateEmail = "anna.petrosyan@ameriabank.am",
        Guid? departmentId = null,
        string position = "Business Analyst",
        string? lineManagerEmail = "manager@ameriabank.am",
        Guid? staffCategoryId = null) =>
        new(
            id ?? Guid.NewGuid(),
            userId ?? UserId,
            employeeId,
            firstName,
            lastName,
            corporateEmail,
            departmentId ?? DepartmentId,
            position,
            lineManagerEmail,
            staffCategoryId ?? CategoryId,
            JoinedAt);

    // ---- creation ----

    [Fact]
    public void Constructor_sets_all_fields_and_starts_Active()
    {
        var id = Guid.NewGuid();

        var member = Create(id: id);

        Assert.Equal(id, member.Id);
        Assert.Equal(UserId, member.UserId);
        Assert.Equal("EMP-0001", member.EmployeeId);
        Assert.Equal("Anna", member.FirstName);
        Assert.Equal("Petrosyan", member.LastName);
        Assert.Equal("anna.petrosyan@ameriabank.am", member.CorporateEmail);
        Assert.Equal(DepartmentId, member.DepartmentId);
        Assert.Equal("Business Analyst", member.Position);
        Assert.Equal("manager@ameriabank.am", member.LineManagerEmail);
        Assert.Equal(CategoryId, member.StaffCategoryId);
        Assert.Equal(JoinedAt, member.JoinedLibraryAtUtc);
        Assert.Equal(StaffMemberStatus.Active, member.Status);
    }

    [Fact]
    public void Constructor_trims_text_and_lowers_the_emails()
    {
        var member = Create(
            employeeId: "  EMP-0002 ",
            firstName: " Anna ",
            lastName: " Petrosyan ",
            corporateEmail: "  Anna.Petrosyan@AmeriaBank.am ",
            position: " Analyst ",
            lineManagerEmail: " Boss@AmeriaBank.am ");

        Assert.Equal("EMP-0002", member.EmployeeId);
        Assert.Equal("Anna", member.FirstName);
        Assert.Equal("Petrosyan", member.LastName);
        Assert.Equal("anna.petrosyan@ameriabank.am", member.CorporateEmail);
        Assert.Equal("Analyst", member.Position);
        Assert.Equal("boss@ameriabank.am", member.LineManagerEmail);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_treats_a_blank_line_manager_as_none(string? lineManagerEmail)
    {
        var member = Create(lineManagerEmail: lineManagerEmail);

        Assert.Null(member.LineManagerEmail);
    }

    [Fact]
    public void Constructor_rejects_an_empty_id()
    {
        Assert.Throws<ArgumentException>(() => Create(id: Guid.Empty));
    }

    [Fact]
    public void Constructor_rejects_an_empty_user_id()
    {
        DomainAssert.ThrowsError(StaffErrors.UserRequired, () => Create(userId: Guid.Empty));
    }

    [Fact]
    public void Constructor_rejects_an_empty_department_id()
    {
        DomainAssert.ThrowsError(StaffErrors.DepartmentRequired, () => Create(departmentId: Guid.Empty));
    }

    [Fact]
    public void Constructor_rejects_an_empty_staff_category_id()
    {
        DomainAssert.ThrowsError(StaffErrors.StaffCategoryRequired, () => Create(staffCategoryId: Guid.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_rejects_a_blank_employee_id(string? value)
    {
        DomainAssert.ThrowsError(StaffErrors.EmployeeIdRequired, () => Create(employeeId: value!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_rejects_a_blank_first_name(string? value)
    {
        DomainAssert.ThrowsError(StaffErrors.FirstNameRequired, () => Create(firstName: value!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_rejects_a_blank_last_name(string? value)
    {
        DomainAssert.ThrowsError(StaffErrors.LastNameRequired, () => Create(lastName: value!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_rejects_a_blank_corporate_email(string? value)
    {
        DomainAssert.ThrowsError(StaffErrors.CorporateEmailRequired, () => Create(corporateEmail: value!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_rejects_a_blank_position(string? value)
    {
        DomainAssert.ThrowsError(StaffErrors.PositionRequired, () => Create(position: value!));
    }

    // ---- profile ----

    [Fact]
    public void UpdateProfile_changes_the_profile_but_not_the_employee_id()
    {
        var member = Create();

        member.UpdateProfile("Mariam", "Hakobyan", "Mariam.Hakobyan@ameriabank.am", "Senior Analyst", null);

        Assert.Equal("Mariam", member.FirstName);
        Assert.Equal("Hakobyan", member.LastName);
        Assert.Equal("mariam.hakobyan@ameriabank.am", member.CorporateEmail);
        Assert.Equal("Senior Analyst", member.Position);
        Assert.Null(member.LineManagerEmail);
        Assert.Equal("EMP-0001", member.EmployeeId);
    }

    [Fact]
    public void UpdateProfile_rejects_a_blank_value()
    {
        var member = Create();

        DomainAssert.ThrowsError(
            StaffErrors.FirstNameRequired,
            () => member.UpdateProfile(" ", "Hakobyan", "m@ameriabank.am", "Analyst", null));
    }

    [Fact]
    public void MoveToDepartment_changes_the_department()
    {
        var member = Create();
        var other = Guid.NewGuid();

        member.MoveToDepartment(other);

        Assert.Equal(other, member.DepartmentId);
    }

    [Fact]
    public void MoveToDepartment_rejects_an_empty_id()
    {
        var member = Create();

        DomainAssert.ThrowsError(StaffErrors.DepartmentRequired, () => member.MoveToDepartment(Guid.Empty));
        Assert.Equal(DepartmentId, member.DepartmentId);
    }

    [Fact]
    public void ChangeCategory_changes_the_category()
    {
        var member = Create();
        var other = Guid.NewGuid();

        member.ChangeCategory(other);

        Assert.Equal(other, member.StaffCategoryId);
    }

    [Fact]
    public void ChangeCategory_rejects_an_empty_id()
    {
        var member = Create();

        DomainAssert.ThrowsError(StaffErrors.StaffCategoryRequired, () => member.ChangeCategory(Guid.Empty));
        Assert.Equal(CategoryId, member.StaffCategoryId);
    }

    // ---- status ----

    [Theory]
    [InlineData(StaffMemberStatus.Restricted)]
    [InlineData(StaffMemberStatus.Suspended)]
    [InlineData(StaffMemberStatus.Active)]
    public void ChangeStatus_sets_Active_Restricted_or_Suspended(StaffMemberStatus status)
    {
        var member = Create();

        member.ChangeStatus(status);

        Assert.Equal(status, member.Status);
    }

    [Fact]
    public void ChangeStatus_can_return_a_Restricted_member_to_Active()
    {
        var member = Create();
        member.ChangeStatus(StaffMemberStatus.Restricted);

        member.ChangeStatus(StaffMemberStatus.Active);

        Assert.Equal(StaffMemberStatus.Active, member.Status);
    }

    [Fact]
    public void ChangeStatus_does_not_accept_Deactivated()
    {
        var member = Create();

        DomainAssert.ThrowsError(StaffErrors.StatusInvalid, () => member.ChangeStatus(StaffMemberStatus.Deactivated));
        Assert.Equal(StaffMemberStatus.Active, member.Status);
    }

    [Fact]
    public void ChangeStatus_does_not_accept_a_value_that_is_not_defined()
    {
        var member = Create();

        DomainAssert.ThrowsError(StaffErrors.StatusInvalid, () => member.ChangeStatus((StaffMemberStatus)0));
    }

    [Theory]
    [InlineData(StaffMemberStatus.Active)]
    [InlineData(StaffMemberStatus.Restricted)]
    [InlineData(StaffMemberStatus.Suspended)]
    public void ChangeStatus_is_refused_for_a_deactivated_member(StaffMemberStatus status)
    {
        var member = Create();
        member.Deactivate();

        DomainAssert.ThrowsError(StaffErrors.DeactivatedCannotChangeStatus, () => member.ChangeStatus(status));
        Assert.Equal(StaffMemberStatus.Deactivated, member.Status);
    }

    // ---- deactivation ----

    [Theory]
    [InlineData(StaffMemberStatus.Active)]
    [InlineData(StaffMemberStatus.Restricted)]
    [InlineData(StaffMemberStatus.Suspended)]
    public void Deactivate_sets_the_status_from_any_working_status(StaffMemberStatus before)
    {
        var member = Create();
        member.ChangeStatus(before);

        member.Deactivate();

        Assert.Equal(StaffMemberStatus.Deactivated, member.Status);
    }

    [Fact]
    public void Deactivate_twice_is_refused()
    {
        var member = Create();
        member.Deactivate();

        DomainAssert.ThrowsError(StaffErrors.AlreadyDeactivated, () => member.Deactivate());
    }
}