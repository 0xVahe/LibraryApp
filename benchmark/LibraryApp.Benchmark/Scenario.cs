using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;

namespace LibraryApp.Benchmark;

/// <summary>Who calls the endpoint: one seeded account per role.</summary>
public enum Caller
{
    StaffMember,
    Librarian,
    Administrator,
}

/// <param name="Name">Shown in the report.</param>
/// <param name="Path">Relative to the API base address, with its query.</param>
/// <param name="LimitMs">NFR-01: 500 ms for a typical call, 2,000 ms for a report.</param>
public sealed record Endpoint(string Name, string Path, Caller Caller, int LimitMs);

/// <summary>
/// The calls that are measured: the read calls behind every page of the application and the three reports, on seeded
/// data. Only reads, so a run does not change the data it measures and can be repeated. The accounts are the seeded
/// Development accounts (README, "Test accounts").
/// </summary>
public static class Scenario
{
    public const int TypicalLimitMs = 500;
    public const int ReportLimitMs = 2_000;

    public static readonly IReadOnlyDictionary<Caller, string> Accounts = new Dictionary<Caller, string>
    {
        [Caller.StaffMember] = "anna.petrosyan@ameriabank.am",
        [Caller.Librarian] = "aram.sargsyan@ameriabank.am",
        [Caller.Administrator] = "lilit.grigoryan@ameriabank.am",
    };

    public static IReadOnlyList<Endpoint> Endpoints(DateOnly today)
    {
        var book = CatalogSeedIds.Book(1);
        var itCategory = CatalogSeedIds.Category(3);   // "IT & Software Engineering", two subcategories
        var anna = StaffSeedIds.StaffMembers.AnnaPetrosyan;
        var yearAgo = today.AddYears(-1);

        return
        [
            // Catalog
            new("Search books", "api/books?search=the&page=1&pageSize=20", Caller.StaffMember, TypicalLimitMs),
            // The Catalog page on a category chip: a top-level category (with its subcategories) and available copies only.
            new("Books of a category, available", $"api/books?categoryId={itCategory}&available=true&page=1&pageSize=20",
                Caller.StaffMember, TypicalLimitMs),
            new("Book details", $"api/books/{book}", Caller.StaffMember, TypicalLimitMs),
            new("Copies of a book", $"api/books/{book}/copies", Caller.Librarian, TypicalLimitMs),
            new("Categories", "api/categories", Caller.StaffMember, TypicalLimitMs),

            // Staff Members
            new("My library", "api/staff-members/me", Caller.StaffMember, TypicalLimitMs),
            new("Search staff members", "api/staff-members?search=an&page=1&pageSize=20", Caller.Librarian, TypicalLimitMs),
            new("Staff member details", $"api/staff-members/{anna}", Caller.Librarian, TypicalLimitMs),
            new("Departments", "api/departments", Caller.Administrator, TypicalLimitMs),

            // Borrowing & Return
            new("Loan history of a member", $"api/loans?staffMemberId={anna}&page=1&pageSize=20", Caller.Librarian, TypicalLimitMs),
            new("Loan history of a book", $"api/loans?bookId={book}&page=1&pageSize=20", Caller.Librarian, TypicalLimitMs),
            new("Own loan history", "api/loans?page=1&pageSize=20", Caller.StaffMember, TypicalLimitMs),
            new("Penalties of a member", $"api/penalties?staffMemberId={anna}", Caller.Librarian, TypicalLimitMs),

            // Notifications
            new("Own notifications", "api/notifications/me", Caller.StaffMember, TypicalLimitMs),

            // Reporting
            new("Report: dashboard", "api/reports/dashboard", Caller.Administrator, ReportLimitMs),
            new("Report: overdue loans", "api/reports/overdue-loans?page=1&pageSize=20", Caller.Administrator, ReportLimitMs),
            new("Report: top books and categories", $"api/reports/top-books-and-categories?from={yearAgo:yyyy-MM-dd}&to={today:yyyy-MM-dd}",
                Caller.Administrator, ReportLimitMs),
        ];
    }
}
