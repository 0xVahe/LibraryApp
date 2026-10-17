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

    /// <summary>
    /// Thirty synthetic staff members across six departments and all three categories.
    /// The original eight retain their IDs, roles and statuses for other modules' seeds and tests.
    /// </summary>
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

        // Retail Banking: 2 existing + 3 new.
        Person(
            StaffMembers.NarekSimonyan,
            9,
            "EMP-10007",
            "Narek",
            "Simonyan",
            Departments.RetailBanking,
            "Branch Manager",
            ManagerEmail,
            Categories.ManagerSenior,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            360
            ),
        Person(
            StaffMembers.EvaHovsepyan,
            10,
            "EMP-10008",
            "Eva",
            "Hovsepyan",
            Departments.RetailBanking,
            "Banking Intern",
            ManagerEmail,
            Categories.InternTrainee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            45
            ),
        Person(
            StaffMembers.TigranMelikyan,
            11,
            "EMP-10009",
            "Tigran",
            "Melikyan",
            Departments.RetailBanking,
            "Customer Adviser",
            ManagerEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            180
            ),

        // Corporate Banking: 1 existing + 4 new.
        Person(
            StaffMembers.ManeGevorgyan,
            12,
            "EMP-10010",
            "Mane",
            "Gevorgyan",
            Departments.CorporateBanking,
            "Corporate Banking Manager",
            ManagerEmail,
            Categories.ManagerSenior,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            330
            ),
        Person(
            StaffMembers.ArmenBabayan,
            13,
            "EMP-10011",
            "Armen",
            "Babayan",
            Departments.CorporateBanking,
            "Corporate Banking Intern",
            ManagerEmail,
            Categories.InternTrainee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            60
            ),
        Person(
            StaffMembers.LusineKarapetyan,
            14,
            "EMP-10012",
            "Lusine",
            "Karapetyan",
            Departments.CorporateBanking,
            "Credit Analyst",
            ManagerEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            210
            ),
        Person(
            StaffMembers.RubenHarutyunyan,
            15,
            "EMP-10013",
            "Ruben",
            "Harutyunyan",
            Departments.CorporateBanking,
            "Relationship Officer",
            ManagerEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            150
            ),

        // Risk Management: 1 existing + 4 new.
        Person(
            StaffMembers.AnahitMartirosyan,
            16,
            "EMP-10014",
            "Anahit",
            "Martirosyan",
            Departments.RiskManagement,
            "Risk Analyst",
            ManagerEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            240
            ),
        Person(
            StaffMembers.VahanStepanyan,
            17,
            "EMP-10015",
            "Vahan",
            "Stepanyan",
            Departments.RiskManagement,
            "Risk Management Intern",
            ManagerEmail,
            Categories.InternTrainee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            35
            ),
        Person(
            StaffMembers.ElenDanielyan,
            18,
            "EMP-10016",
            "Elen",
            "Danielyan",
            Departments.RiskManagement,
            "Compliance Analyst",
            ManagerEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            190
            ),
        Person(
            StaffMembers.SamvelPoghosyan,
            19,
            "EMP-10017",
            "Samvel",
            "Poghosyan",
            Departments.RiskManagement,
            "Operational Risk Analyst",
            ManagerEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            280
            ),

        // Information Technology: 2 existing + 3 new.
        Person(
            StaffMembers.AniSahakyan,
            20,
            "EMP-10018",
            "Ani",
            "Sahakyan",
            Departments.InformationTechnology,
            "Software Developer",
            AdminEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            220
            ),
        Person(
            StaffMembers.LevonKhachatryan,
            21,
            "EMP-10019",
            "Levon",
            "Khachatryan",
            Departments.InformationTechnology,
            "Senior Systems Engineer",
            AdminEmail,
            Categories.ManagerSenior,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            390
            ),
        Person(
            StaffMembers.MilenaAbrahamyan,
            22,
            "EMP-10020",
            "Milena",
            "Abrahamyan",
            Departments.InformationTechnology,
            "Quality Assurance Engineer",
            AdminEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            160
            ),

        // Finance: 1 existing + 4 new.
        Person(
            StaffMembers.ArturMinasyan,
            23,
            "EMP-10021",
            "Artur",
            "Minasyan",
            Departments.Finance,
            "Finance Manager",
            ManagerEmail,
            Categories.ManagerSenior,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            350
            ),
        Person(
            StaffMembers.LianaNalbandyan,
            24,
            "EMP-10022",
            "Liana",
            "Nalbandyan",
            Departments.Finance,
            "Finance Intern",
            ManagerEmail,
            Categories.InternTrainee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            50
            ),
        Person(
            StaffMembers.KarenAsatryan,
            25,
            "EMP-10023",
            "Karen",
            "Asatryan",
            Departments.Finance,
            "Financial Analyst",
            ManagerEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            200
            ),
        Person(
            StaffMembers.SofiaHambardzumyan,
            26,
            "EMP-10024",
            "Sofia",
            "Hambardzumyan",
            Departments.Finance,
            "Accountant",
            ManagerEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            130
            ),

        // Human Resources: 1 existing + 4 new.
        Person(
            StaffMembers.AshotBaghdasaryan,
            27,
            "EMP-10025",
            "Ashot",
            "Baghdasaryan",
            Departments.HumanResources,
            "Human Resources Manager",
            ManagerEmail,
            Categories.ManagerSenior,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            320
            ),
        Person(
            StaffMembers.NuneTorosyan,
            28,
            "EMP-10026",
            "Nune",
            "Torosyan",
            Departments.HumanResources,
            "Human Resources Intern",
            ManagerEmail,
            Categories.InternTrainee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            40
            ),
        Person(
            StaffMembers.ErikZakaryan,
            29,
            "EMP-10027",
            "Erik",
            "Zakaryan",
            Departments.HumanResources,
            "Recruitment Specialist",
            ManagerEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            170
            ),
        Person(
            StaffMembers.AlinaMirzoyan,
            30,
            "EMP-10028",
            "Alina",
            "Mirzoyan",
            Departments.HumanResources,
            "Training Specialist",
            ManagerEmail,
            Categories.Employee,
            StaffRoles.StaffMember,
            StaffMemberStatus.Active,
            110
            ),
    ];

    private static SeedPerson Person(
        Guid staffMemberId, int number, string employeeId, string firstName, string lastName, Guid departmentId,
        string position, string? lineManagerEmail, Guid categoryId, string role, StaffMemberStatus status, int joinedDaysAgo) =>
        new(new Guid($"20000000-0000-0000-0004-{number:D12}"), staffMemberId, employeeId, firstName, lastName,
            $"{firstName}.{lastName}@ameriabank.am".ToLowerInvariant(), departmentId, position, lineManagerEmail,
            categoryId, role, status, joinedDaysAgo);
}