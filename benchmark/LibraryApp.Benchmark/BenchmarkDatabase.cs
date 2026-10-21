using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Benchmark;

/// <summary>
/// The loans NFR-01 is measured on. <see cref="SeedAsync"/> adds returned loans until the database holds the target
/// number; they are built by <see cref="Loan.Issue"/> and <see cref="Loan.Return"/>, so they follow the domain rules.
/// They are saved through a plain <see cref="LibraryDbContext"/> without the application's interceptors: no audit rows
/// and no events, because this is test data and not a library operation. Every added loan has an id that starts with
/// <see cref="IdPrefix"/>, which is how <see cref="CleanAsync"/> finds them again.
/// </summary>
public sealed class BenchmarkDatabase
{
    /// <summary>The first 24 characters of every benchmark loan id; the last 12 are its number.</summary>
    public const string IdPrefix = "be7c0000-0000-0000-0000-";

    private const int BatchSize = 1_000;

    private readonly string _connectionString;

    private BenchmarkDatabase(string connectionString) => _connectionString = connectionString;

    /// <summary>Server and database, without credentials; shown in the report.</summary>
    public string Description
    {
        get
        {
            var builder = new SqlConnectionStringBuilder(_connectionString);
            return $"{builder.DataSource} / {builder.InitialCatalog}";
        }
    }

    /// <summary>The database of <c>ConnectionStrings__LibraryDb</c> (or <c>--connection</c>); a local server only, unless allowed.</summary>
    public static BenchmarkDatabase Open(BenchmarkArguments arguments) =>
        TryOpen(arguments) ?? throw new BenchmarkException(
            "no connection string: set ConnectionStrings__LibraryDb (the repository .env has it) or pass --connection.");

    /// <summary>As <see cref="Open"/>, or null when no connection string is set (the run report then has no database section).</summary>
    public static BenchmarkDatabase? TryOpen(BenchmarkArguments arguments)
    {
        var connectionString = arguments.ConnectionString ?? Environment.GetEnvironmentVariable("ConnectionStrings__LibraryDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            throw new BenchmarkException($"the connection string is not valid: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            throw new BenchmarkException("the connection string names no database (Database=...).");

        if (!arguments.AllowRemote && !Hosts.IsLocal(builder.DataSource))
            throw new BenchmarkException($"'{builder.DataSource}' is not a local server; pass --allow-remote to use it anyway.");

        return new BenchmarkDatabase(connectionString);
    }

    public async Task<int> SeedAsync(int totalLoans)
    {
        await using var db = NewContext();
        var existing = await db.Loans.CountAsync();
        var missing = totalLoans - existing;
        if (missing <= 0)
        {
            Console.WriteLine($"{Description}: {existing} loans already, target {totalLoans}; nothing added.");
            return 0;
        }

        var members = await db.StaffMembers.AsNoTracking()
            .Where(m => m.Status != StaffMemberStatus.Deactivated)
            .OrderBy(m => m.Id).Select(m => m.Id).ToListAsync();
        var copies = await db.BookCopies.AsNoTracking()
            .Where(c => c.Status != BookCopyStatus.Lost && c.Status != BookCopyStatus.Withdrawn)
            .OrderBy(c => c.Id).Select(c => c.Id).ToListAsync();
        if (members.Count == 0 || copies.Count == 0)
            throw new BenchmarkException($"{Description} has no staff members or no copies: start the API in Development once so it seeds them.");

        var nextNumber = await NextNumberAsync(db);
        var loans = SyntheticLoans.Create(missing, nextNumber, members, copies, DateTime.UtcNow);

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        foreach (var batch in loans.Chunk(BatchSize))
        {
            db.Loans.AddRange(batch);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        Console.WriteLine($"{Description}: added {missing} returned loans ({members.Count} staff members, {copies.Count} copies); " +
                          $"{existing + missing} loans in total.");
        return 0;
    }

    public async Task<int> CleanAsync()
    {
        await using var db = NewContext();
        var removed = await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM " + LoansTable(db) + " WHERE " + IsBenchmarkLoan, PrefixParameter());
        Console.WriteLine($"{Description}: removed {removed} benchmark loans.");
        return 0;
    }

    /// <summary>Row counts and server version for the report manifest.</summary>
    public async Task<DatabaseSnapshot> SnapshotAsync()
    {
        await using var db = NewContext();
        var version = (await db.Database.SqlQueryRaw<string>("SELECT @@VERSION AS [Value]").ToListAsync()).Single();
        var benchmarkLoans = (await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM " + LoansTable(db) + " WHERE " + IsBenchmarkLoan, PrefixParameter()).ToListAsync()).Single();

        return new DatabaseSnapshot(
            Description,
            version.Split('\n')[0].Trim(),
            await db.Loans.CountAsync(),
            await db.Loans.CountAsync(l => l.Status == LoanStatus.Active || l.Status == LoanStatus.Overdue),
            benchmarkLoans,
            await db.Books.CountAsync(),
            await db.BookCopies.CountAsync(),
            await db.StaffMembers.CountAsync(),
            await db.Reservations.CountAsync(),
            await db.PenaltyRecords.CountAsync(),
            await db.Notifications.CountAsync());
    }

    private const string IsBenchmarkLoan = "CONVERT(char(36), [Id]) LIKE @prefix";

    private static SqlParameter PrefixParameter() => new("@prefix", IdPrefix + "%");

    private LibraryDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>();
        options.UseLibrarySqlServer(_connectionString);
        return new LibraryDbContext(options.Options);
    }

    /// <summary>The table of <see cref="Loan"/> from the EF model, quoted.</summary>
    private static string LoansTable(LibraryDbContext db)
    {
        var entity = db.Model.FindEntityType(typeof(Loan))!;
        return $"[{entity.GetSchema() ?? "dbo"}].[{entity.GetTableName()}]";
    }

    /// <summary>The number after the highest benchmark loan, so a second seed continues the series.</summary>
    private static async Task<long> NextNumberAsync(LibraryDbContext db)
    {
        var highest = (await db.Database.SqlQueryRaw<string>(
            "SELECT MAX(CONVERT(char(36), [Id])) AS [Value] FROM " + LoansTable(db) + " WHERE " + IsBenchmarkLoan, PrefixParameter())
            .ToListAsync()).SingleOrDefault();
        return highest is null ? 1 : long.Parse(highest[IdPrefix.Length..]) + 1;
    }
}

/// <param name="Server">Server and database, without credentials.</param>
public sealed record DatabaseSnapshot(
    string Server,
    string Version,
    int Loans,
    int OpenLoans,
    int BenchmarkLoans,
    int Books,
    int Copies,
    int StaffMembers,
    int Reservations,
    int PenaltyRecords,
    int Notifications);

/// <summary>Host names that mean "this machine".</summary>
public static class Hosts
{
    public static bool IsLocal(string host)
    {
        // "tcp:localhost,1433", "(localdb)\MSSQLLocalDB", ".\SQLEXPRESS", "[::1]:5055"
        var name = host.Trim();
        if (name.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            name = name[4..];
        name = name.Split(',', '\\')[0].Trim('[', ']');

        return name is "." or "localhost" or "127.0.0.1" or "::1"
               || name.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }
}
