using LibraryApp.Domain.Staff;

namespace LibraryApp.Domain.Tests.Staff;

public class StaffCategoryTests
{
    // The default categories of the requirements document (section 7).
    [Theory]
    [InlineData("Intern / Trainee", 2, 14, 1)]
    [InlineData("Employee", 3, 21, 1)]
    [InlineData("Manager / Senior Specialist", 5, 30, 2)]
    public void Constructor_accepts_the_default_categories(string name, int maxLoans, int loanDays, int maxRenewals)
    {
        var category = new StaffCategory(Guid.NewGuid(), name, maxLoans, loanDays, maxRenewals);

        Assert.Equal(name, category.Name);
        Assert.Equal(maxLoans, category.MaxLoans);
        Assert.Equal(loanDays, category.LoanDays);
        Assert.Equal(maxRenewals, category.MaxRenewals);
    }

    [Fact]
    public void Constructor_allows_zero_renewals()
    {
        var category = new StaffCategory(Guid.NewGuid(), "No renewals", 1, 7, 0);

        Assert.Equal(0, category.MaxRenewals);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_rejects_a_blank_name(string? name)
    {
        DomainAssert.ThrowsError(StaffErrors.CategoryNameRequired, () => new StaffCategory(Guid.NewGuid(), name!, 3, 21, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_max_loans_below_one(int maxLoans)
    {
        DomainAssert.ThrowsError(StaffErrors.MaxLoansInvalid, () => new StaffCategory(Guid.NewGuid(), "Employee", maxLoans, 21, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_rejects_loan_days_below_one(int loanDays)
    {
        DomainAssert.ThrowsError(StaffErrors.LoanDaysInvalid, () => new StaffCategory(Guid.NewGuid(), "Employee", 3, loanDays, 1));
    }

    [Fact]
    public void Constructor_rejects_negative_max_renewals()
    {
        DomainAssert.ThrowsError(StaffErrors.MaxRenewalsInvalid, () => new StaffCategory(Guid.NewGuid(), "Employee", 3, 21, -1));
    }

    [Fact]
    public void SetLimits_changes_all_three_limits()
    {
        var category = new StaffCategory(Guid.NewGuid(), "Employee", 3, 21, 1);

        category.SetLimits(4, 28, 2);

        Assert.Equal(4, category.MaxLoans);
        Assert.Equal(28, category.LoanDays);
        Assert.Equal(2, category.MaxRenewals);
    }

    [Fact]
    public void SetLimits_with_an_invalid_value_changes_nothing()
    {
        var category = new StaffCategory(Guid.NewGuid(), "Employee", 3, 21, 1);

        DomainAssert.ThrowsError(StaffErrors.LoanDaysInvalid, () => category.SetLimits(5, 0, 2));

        Assert.Equal(3, category.MaxLoans);
        Assert.Equal(21, category.LoanDays);
        Assert.Equal(1, category.MaxRenewals);
    }
}