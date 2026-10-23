namespace LibraryApp.Application.Reporting.Options;

public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    public int DashboardCacheMinutes { get; set; }

    public int TopBooksLimit { get; set; }

    public int TopCategoriesLimit { get; set; }

    public int PurchaseCandidatesLimit { get; set; }
}
