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

        // Retail Banking Department
        public static readonly Guid NarekSimonyan = new("20000000-0000-0000-0003-000000000009");        // Manager, Active
        public static readonly Guid EvaHovsepyan = new("20000000-0000-0000-0003-000000000010");         // Intern, Active
        public static readonly Guid TigranMelikyan = new("20000000-0000-0000-0003-000000000011");       // Employee, Active

        // Corporate Banking Department
        public static readonly Guid ManeGevorgyan = new("20000000-0000-0000-0003-000000000012");        // Manager, Active
        public static readonly Guid ArmenBabayan = new("20000000-0000-0000-0003-000000000013");         // Intern, Active
        public static readonly Guid LusineKarapetyan = new("20000000-0000-0000-0003-000000000014");     // Employee, Active
        public static readonly Guid RubenHarutyunyan = new("20000000-0000-0000-0003-000000000015");     // Employee, Active

        // Risk Management Department
        public static readonly Guid AnahitMartirosyan = new("20000000-0000-0000-0003-000000000016");    // Employee, Active
        public static readonly Guid VahanStepanyan = new("20000000-0000-0000-0003-000000000017");       // Intern, Active
        public static readonly Guid ElenDanielyan = new("20000000-0000-0000-0003-000000000018");        // Employee, Active
        public static readonly Guid SamvelPoghosyan = new("20000000-0000-0000-0003-000000000019");      // Employee, Active

        // Information Technology Department
        public static readonly Guid AniSahakyan = new("20000000-0000-0000-0003-000000000020");          // Employee, Active
        public static readonly Guid LevonKhachatryan = new("20000000-0000-0000-0003-000000000021");     // Manager, Active
        public static readonly Guid MilenaAbrahamyan = new("20000000-0000-0000-0003-000000000022");     // Employee, Active

        // Finance Department
        public static readonly Guid ArturMinasyan = new("20000000-0000-0000-0003-000000000023");        // Manager, Active
        public static readonly Guid LianaNalbandyan = new("20000000-0000-0000-0003-000000000024");      // Intern, Active
        public static readonly Guid KarenAsatryan = new("20000000-0000-0000-0003-000000000025");        // Employee, Active
        public static readonly Guid SofiaHambardzumyan = new("20000000-0000-0000-0003-000000000026");   // Employee, Active

        // Human Resources Department
        public static readonly Guid AshotBaghdasaryan = new("20000000-0000-0000-0003-000000000027");    // Manager, Active
        public static readonly Guid NuneTorosyan = new("20000000-0000-0000-0003-000000000028");         // Intern, Active
        public static readonly Guid ErikZakaryan = new("20000000-0000-0000-0003-000000000029");         // Employee, Active
        public static readonly Guid AlinaMirzoyan = new("20000000-0000-0000-0003-000000000030");        // Employee, Active
    }
}