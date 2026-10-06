using LibraryApp.Domain.Common;
 
namespace LibraryApp.Domain.Staff;
 
/// <summary>A department of the bank (MEM-06). Seeded with 6 departments.</summary>
public sealed class Department : Entity
{
    private Department() { } // for EF Core only
 
    public Department(Guid id, string name) : base(id)
    {
        Rename(name);
    }
 
    public string Name { get; private set; } = null!;
 
    public void Rename(string name) =>
        Name = StaffGuard.RequireText(name, StaffErrors.DepartmentNameRequired);
}