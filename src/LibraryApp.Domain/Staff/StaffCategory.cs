using LibraryApp.Domain.Common;
 
namespace LibraryApp.Domain.Staff;
 
/// <summary>
/// Lending limits of a group of employees (MEM-04): Intern / Trainee, Employee, Manager / Senior Specialist.
/// Configurable by the Library Administrator.
/// </summary>
public sealed class StaffCategory : Entity
{
    private StaffCategory() { } // for EF Core only
 
    public StaffCategory(Guid id, string name, int maxLoans, int loanDays, int maxRenewals) : base(id)
    {
        Rename(name);
        SetLimits(maxLoans, loanDays, maxRenewals);
    }
 
    public string Name { get; private set; } = null!;
 
    /// <summary>How many loans the employee may hold at the same time.</summary>
    public int MaxLoans { get; private set; }
 
    /// <summary>How many days a loan lasts (and how many days one renewal adds).</summary>
    public int LoanDays { get; private set; }
 
    public int MaxRenewals { get; private set; }
 
    public void Rename(string name) =>
        Name = StaffGuard.RequireText(name, StaffErrors.CategoryNameRequired);
 
    public void SetLimits(int maxLoans, int loanDays, int maxRenewals)
    {
        if (maxLoans <= 0)
            throw new DomainException(StaffErrors.MaxLoansInvalid);
 
        if (loanDays <= 0)
            throw new DomainException(StaffErrors.LoanDaysInvalid);
 
        if (maxRenewals < 0)
            throw new DomainException(StaffErrors.MaxRenewalsInvalid);
 
        MaxLoans = maxLoans;
        LoanDays = loanDays;
        MaxRenewals = maxRenewals;
    }
}