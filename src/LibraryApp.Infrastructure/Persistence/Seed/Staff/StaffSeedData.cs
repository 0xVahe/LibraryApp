using LibraryApp.Domain.Staff;
using static LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds;

namespace LibraryApp.Infrastructure.Persistence.Seed.Staff;

public sealed record SeedDepartment(Guid Id, string Name);

public sealed record SeedCategory(Guid Id, string Name, int MaxLoans, int LoanDays, int MaxRenewals);

public sealed record SeedPerson(
    Guid UserId,
    Guid StaffMemberId,
    string EmployeeId,
    string FirstName,
    string LastName,
    string Email,
    Guid DepartmentId,
    string Position,
    string? LineManagerEmail,
    Guid CategoryId,
    string Role,
    StaffMemberStatus Status,
    int JoinedDaysAgo);

/// <summary>Synthetic Staff demo data (C-2). Development only: every account uses <see cref="Password"/>.</summary>
public static class StaffSeedData
{
    /// <summary>Meets BR-MEM-5 (8+ characters, a letter and a digit).</summary>
    public const string Password = "Library2026";

    private const string ManagerEmail = "mariam.avetisyan@ameriabank.am";
    private const string AdminEmail = "lilit.grigoryan@ameriabank.am";

    /// <summary>MEM-06: six departments.</summary>
    public static readonly IReadOnlyList<SeedDepartment> DepartmentList =
    [
        new(Departments.RetailBanking, "Retail Banking"),
        new(Departments.CorporateBanking, "Corporate Banking"),
        new(Departments.RiskManagement, "Risk Management"),
        new(Departments.InformationTechnology, "Information Technology"),
        new(Departments.Finance, "Finance"),
        new(Departments.HumanResources, "Human Resources"),
    ];

    /// <summary>Default staff categories of the requirements (MEM-04).</summary>
    public static readonly IReadOnlyList<SeedCategory> CategoryList =
    [
        new(Categories.InternTrainee, "Intern / Trainee", MaxLoans: 2, LoanDays: 14, MaxRenewals: 1),
        new(Categories.Employee, "Employee", MaxLoans: 3, LoanDays: 21, MaxRenewals: 1),
        new(Categories.ManagerSenior, "Manager / Senior Specialist", MaxLoans: 5, LoanDays: 30, MaxRenewals: 2),
    ];

    /// <summary>One account per role and per status, so every module can test its rules.</summary>
    public static readonly IReadOnlyList<SeedPerson> People =
    [
        Person(
            StaffMembers.LilitGrigoryan,
            1, "EMP-90001",
            "Lilit", 
            "Grigoryan", 
            Departments.InformationTechnology,
            "Library Administrator", 
            null, 
            Categories.ManagerSenior, 
            StaffRoles.LibraryAdministrator, 
            StaffMemberStatus.Active, 
            365
            ),
        Person(
            StaffMembers.AramSargsyan, 
            2, 
            "EMP-90002", 
            "Aram", 
            "Sargsyan",
            Departments.HumanResources,
            "Librarian",
            AdminEmail,
            Categories.Employee,
            StaffRoles.Librarian,
            StaffMemberStatus.Active,
            300
            ),
        Person(
            StaffMembers.AnnaPetrosyan,
            3,
            "EMP-10001",
            "Anna",
            "Petrosyan",
            Departments.RetailBanking,
            "Credit Analyst",
            ManagerEmail, 
            Categories.Employee,
            StaffRoles.StaffMember, 
            StaffMemberStatus.Active, 
            200),
        Person(
            StaffMembers.DavitHakobyan,
            4, "EMP-10002",
            "Davit", 
            "Hakobyan", 
            Departments.InformationTechnology,
            "Intern Developer",
            AdminEmail,
            Categories.InternTrainee,
            StaffRoles.StaffMember, 
            StaffMemberStatus.Active,
            30
            ),
        Person(
            StaffMembers.MariamAvetisyan,
            5, 
            "EMP-10003",
            "Mariam", 
            "Avetisyan", 
            Departments.RiskManagement,
            "Senior Risk Manager", 
            null, 
            Categories.ManagerSenior, 
            StaffRoles.StaffMember, 
            StaffMemberStatus.Active, 
            400
            ),
        Person(
            StaffMembers.GorMkrtchyan, 
            6, 
            "EMP-10004", 
            "Gor", 
            "Mkrtchyan", 
            Departments.Finance,
            "Accountant",
            ManagerEmail, 
            Categories.Employee, 
            StaffRoles.StaffMember, 
            StaffMemberStatus.Restricted, 
            150
            ),
        Person(
            StaffMembers.SonaVardanyan,
            7,
            "EMP-10005",
            "Sona",
            "Vardanyan",
            Departments.CorporateBanking,
            "Relationship Manager",
            ManagerEmail, 
            Categories.Employee, 
            StaffRoles.StaffMember,
            StaffMemberStatus.Suspended,
            120
            ),
        Person(
            StaffMembers.HaykManukyan,
            8, 
            "EMP-10006",
            "Hayk",
            "Manukyan", 
            Departments.RetailBanking,
            "Teller",
            ManagerEmail, 
            Categories.Employee, 
            StaffRoles.StaffMember,
            StaffMemberStatus.Deactivated,
            500
            ),
    ];

    private static SeedPerson Person(
        Guid staffMemberId, int number, string employeeId, string firstName, string lastName, Guid departmentId,
        string position, string? lineManagerEmail, Guid categoryId, string role, StaffMemberStatus status, int joinedDaysAgo) =>
        new(new Guid($"20000000-0000-0000-0004-{number:D12}"), staffMemberId, employeeId, firstName, lastName,
            $"{firstName}.{lastName}@ameriabank.am".ToLowerInvariant(), departmentId, position, lineManagerEmail,
            categoryId, role, status, joinedDaysAgo);
}