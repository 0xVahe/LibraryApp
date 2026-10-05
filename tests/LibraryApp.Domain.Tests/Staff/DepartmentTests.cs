using LibraryApp.Domain.Staff;

namespace LibraryApp.Domain.Tests.Staff;

public class DepartmentTests
{
    [Fact]
    public void Constructor_sets_id_and_trimmed_name()
    {
        var id = Guid.NewGuid();

        var department = new Department(id, "  Risk & Compliance  ");

        Assert.Equal(id, department.Id);
        Assert.Equal("Risk & Compliance", department.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_rejects_a_blank_name(string? name)
    {
        DomainAssert.ThrowsError(StaffErrors.DepartmentNameRequired, () => new Department(Guid.NewGuid(), name!));
    }

    [Fact]
    public void Constructor_rejects_an_empty_id()
    {
        Assert.Throws<ArgumentException>(() => new Department(Guid.Empty, "IT"));
    }

    [Fact]
    public void Rename_changes_the_name()
    {
        var department = new Department(Guid.NewGuid(), "IT");

        department.Rename("Information Technology");

        Assert.Equal("Information Technology", department.Name);
    }

    [Fact]
    public void Rename_rejects_a_blank_name_and_keeps_the_old_one()
    {
        var department = new Department(Guid.NewGuid(), "IT");

        DomainAssert.ThrowsError(StaffErrors.DepartmentNameRequired, () => department.Rename(" "));

        Assert.Equal("IT", department.Name);
    }
}