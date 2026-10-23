using System.Text.RegularExpressions;

namespace LibraryApp.Infrastructure.IntegrationTests.Borrowing;

/// <summary>
/// Integration rules of the requirements: modules talk to each other only through the service interfaces and domain
/// events, with no direct cross-module table access. Borrowing's Infrastructure code reads only Loans, Reservations and
/// PenaltyRecords; books, copies and staff come through <c>ICatalogService</c> and <c>IStaffMemberService</c>.
/// </summary>
public sealed class BorrowingModuleBoundaryTests
{
    // Tables of Catalog, Staff Members and Notifications in LibraryDbContext.
    private static readonly Regex OtherModuleTable = new(
        @"\bdb\.(Books|BookCopies|BookAuthors|Authors|Categories|Publishers|BookSuggestions|StaffMembers|Departments|StaffCategories|Users|Roles|UserRoles|Notifications|NotificationTemplates)\b|\bSet<(Book|BookCopy|Author|Category|Publisher|BookSuggestion|StaffMember|Department|StaffCategory|Notification|NotificationTemplate)>",
        RegexOptions.Compiled);

    [Fact]
    public void Borrowing_reads_no_table_of_another_module()
    {
        string infrastructure = Path.Combine(RepositoryRoot(), "src", "LibraryApp.Infrastructure");
        string[] files =
        [
            .. Directory.EnumerateFiles(Path.Combine(infrastructure, "Persistence", "Repositories", "Borrowing"), "*.cs"),
            .. Directory.EnumerateFiles(Path.Combine(infrastructure, "Jobs", "Borrowing"), "*.cs"),
        ];
        Assert.NotEmpty(files);

        var offending = files
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(x => OtherModuleTable.IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.number}: {x.line.Trim()}")
            .ToList();

        Assert.True(offending.Count == 0,
            "Borrowing reads another module's table; use its service interface instead:" + Environment.NewLine +
            string.Join(Environment.NewLine, offending));
    }

    [Theory]
    [InlineData("join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id")]
    [InlineData("var staff = await db.StaffMembers.FindAsync(id);")]
    [InlineData("db.Set<Book>().Where(b => b.Id == id)")]
    public void The_check_recognises_a_read_of_another_module(string line) =>
        Assert.Matches(OtherModuleTable, line);

    [Theory]
    [InlineData("from loan in db.Loans.AsNoTracking()")]
    [InlineData("await catalog.GetCopiesAsync(ids, ct);")]
    [InlineData("loan.BookCopyId")]
    public void The_check_lets_Borrowing_read_its_own_tables_and_contracts(string line) =>
        Assert.DoesNotMatch(OtherModuleTable, line);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LibraryApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("LibraryApp.sln was not found above the test folder.");
    }
}
