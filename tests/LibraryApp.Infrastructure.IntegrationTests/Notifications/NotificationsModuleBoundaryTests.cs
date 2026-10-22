using System.Text.RegularExpressions;

namespace LibraryApp.Infrastructure.IntegrationTests.Notifications;

/// <summary>
/// Integration rules of the requirements: modules talk to each other only through the service interfaces and domain
/// events, with no direct cross-module table access. Notifications' Infrastructure code reads only Notifications and
/// NotificationTemplates; staff members (name, emails, status) come through <c>IStaffMemberService.GetContactsAsync</c>,
/// loans through <c>IBorrowingReadService</c> and books through <c>ICatalogService</c>.
/// </summary>
public sealed class NotificationsModuleBoundaryTests
{
    // Tables of Catalog, Staff Members (with the identity tables) and Borrowing in LibraryDbContext.
    private static readonly Regex OtherModuleTable = new(
        @"\bdb\.(Books|BookCopies|BookAuthors|Authors|Categories|Publishers|BookSuggestions|StaffMembers|Departments|StaffCategories|Users|Roles|UserRoles|Loans|Reservations|PenaltyRecords)\b|\bSet<(Book|BookCopy|BookAuthor|Author|Category|Publisher|BookSuggestion|StaffMember|Department|StaffCategory|Loan|Reservation|PenaltyRecord)>",
        RegexOptions.Compiled);

    [Fact]
    public void Notifications_reads_no_table_of_another_module()
    {
        string infrastructure = Path.Combine(RepositoryRoot(), "src", "LibraryApp.Infrastructure");
        string[] files =
        [
            .. Directory.EnumerateFiles(Path.Combine(infrastructure, "Persistence", "Repositories", "Notifications"), "*.cs"),
            .. Directory.EnumerateFiles(Path.Combine(infrastructure, "Jobs", "Notifications"), "*.cs"),
        ];
        Assert.NotEmpty(files);

        var offending = files
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(x => OtherModuleTable.IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.number}: {x.line.Trim()}")
            .ToList();

        Assert.True(offending.Count == 0,
            "Notifications reads another module's table; use its service interface instead:" + Environment.NewLine +
            string.Join(Environment.NewLine, offending));
    }

    [Theory]
    [InlineData("db.StaffMembers.AsNoTracking()")]
    [InlineData("from loan in db.Loans.AsNoTracking()")]
    [InlineData("join copy in db.BookCopies.AsNoTracking() on loan.BookCopyId equals copy.Id")]
    [InlineData("join book in db.Books.AsNoTracking() on copy.BookId equals book.Id")]
    [InlineData("db.Set<StaffMember>().Where(x => x.Id == id)")]
    public void The_check_recognises_a_read_of_another_module(string line) =>
        Assert.Matches(OtherModuleTable, line);

    [Theory]
    [InlineData("db.Notifications.AsNoTracking().Where(n => n.EventId == eventId)")]
    [InlineData("db.NotificationTemplates.SingleOrDefaultAsync(t => t.Id == id, ct)")]
    [InlineData("await db.SaveChangesAsync(ct);")]
    [InlineData("var contacts = await staff.GetContactsAsync(ids, ct);")]
    [InlineData("notification.StaffMemberId")]
    public void The_check_lets_Notifications_read_its_own_tables_and_contracts(string line) =>
        Assert.DoesNotMatch(OtherModuleTable, line);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LibraryApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("LibraryApp.sln was not found above the test folder.");
    }
}
