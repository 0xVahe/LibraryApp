namespace LibraryApp.Application.Reporting.Options;

public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    public int DashboardCacheMinutes { get; set; } = 5;

    public int TopBooksLimit { get; set; } = 10;

    public int TopCategoriesLimit { get; set; } = 5;
}
