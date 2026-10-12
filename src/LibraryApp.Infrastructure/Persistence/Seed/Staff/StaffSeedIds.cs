namespace LibraryApp.Infrastructure.Persistence.Seed.Staff;

/// <summary>
/// Constant ids of the Staff seed (SD-17). Other modules' seeders reference these, so they never change.
/// Pattern: 20000000-0000-0000-&lt;kind&gt;-&lt;number&gt; (2 = Staff module).
/// </summary>
public static class StaffSeedIds
{
    public static class Departments
    {
        public static readonly Guid RetailBanking = new("20000000-0000-0000-0001-000000000001");
        public static readonly Guid CorporateBanking = new("20000000-0000-0000-0001-000000000002");
        public static readonly Guid RiskManagement = new("20000000-0000-0000-0001-000000000003");
        public static readonly Guid InformationTechnology = new("20000000-0000-0000-0001-000000000004");
        public static readonly Guid Finance = new("20000000-0000-0000-0001-000000000005");
        public static readonly Guid HumanResources = new("20000000-0000-0000-0001-000000000006");
    }

    public static class Categories
    {
        public static readonly Guid InternTrainee = new("20000000-0000-0000-0002-000000000001");
        public static readonly Guid Employee = new("20000000-0000-0000-0002-000000000002");
        public static readonly Guid ManagerSenior = new("20000000-0000-0000-0002-000000000003");
    }

    public static class StaffMembers
    {
        public static readonly Guid LilitGrigoryan = new("20000000-0000-0000-0003-000000000001");   // Library Administrator
        public static readonly Guid AramSargsyan = new("20000000-0000-0000-0003-000000000002");     // Librarian
        public static readonly Guid AnnaPetrosyan = new("20000000-0000-0000-0003-000000000003");    // Employee, Active
        public static readonly Guid DavitHakobyan = new("20000000-0000-0000-0003-000000000004");    // Intern, Active
        public static readonly Guid MariamAvetisyan = new("20000000-0000-0000-0003-000000000005");  // Manager, Active
        public static readonly Guid GorMkrtchyan = new("20000000-0000-0000-0003-000000000006");     // Restricted
        public static readonly Guid SonaVardanyan = new("20000000-0000-0000-0003-000000000007");    // Suspended
        public static readonly Guid HaykManukyan = new("20000000-0000-0000-0003-000000000008");     // Deactivated
    }
}