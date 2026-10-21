using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Application.Borrowing.Options;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ids = LibraryApp.Infrastructure.Persistence.Seed.Borrowing.BorrowingSeedIds;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Infrastructure.Persistence.Seed.Borrowing;

/// <summary>
/// Demo data of Borrowing (Development only, synthetic C-2): 40 loans in every status, 6 of them overdue (the
/// historical loans of the requirements scope and the REP-02 acceptance example: overdue rate 15.0 %), a waiting queue, a
/// copy held for pickup and penalty records in every status, matching the statuses of the Staff seed (Gor Restricted with
/// 6 active points, Sona Suspended with 10, Hayk Deactivated without open loans).
/// <list type="bullet">
/// <item>Book 1 (3 copies): all on loan (Anna, Mariam, Davit); Aram and Lilit wait in the queue.</item>
/// <item>Book 15 (4 copies): Gor's active loan, Anna's overdue loan, Sona's lost copy, the copy held for Mariam.</item>
/// <item>Book 16 (3 copies): returned loans only — the history behind the penalties.</item>
/// <item>Copy 1 of other books: loans of the other staff members across the six departments — five more overdue loans,
/// recent active loans and loans returned on time (no penalty).</item>
/// </list>
/// The figures hold for at least 12 days after seeding: an overdue loan stays overdue, and every other open loan is due
/// 12 or more days after the seeding day. Recreate the Development database to start the 12 days again (README).
/// Dates are relative to <see cref="IClock.UtcNow"/>; late returns are counted in working days (BR-BOR-5). Points, expiry
/// and the pickup deadline come from the Borrowing settings; the staff statuses above match the default values. Entities are
/// created through their own methods, copy statuses change only through <see cref="ICatalogService"/>, no event is
/// published (demo data must not send emails). Runs inside the seed runner's transaction.
/// Idempotent: nothing is added when the first seeded loan exists. Skipped with a warning when the staff members or
/// copies it needs are missing or a needed copy is no longer Available.
/// </summary>
public sealed class BorrowingSeeder(
    LibraryDbContext db,
    ICatalogService catalog,
    IStaffMemberService staff,
    IClock clock,
    IWorkingDayCalendar calendar,
    PenaltyPolicy policy,
    IOptions<BorrowingOptions> options,
    ILogger<BorrowingSeeder> logger) : IModuleSeeder
{
    // Shape of the demo data, not a rule: every late return in the seed is this many working days late.
    private const int LateWorkingDays = 3;

    private static readonly Guid[] StaffMembers =
    [
        Members.AnnaPetrosyan, Members.MariamAvetisyan, Members.DavitHakobyan, Members.AramSargsyan,
        Members.LilitGrigoryan, Members.GorMkrtchyan, Members.SonaVardanyan, Members.HaykManukyan,
    ];

    // Open loans past their due date (book, copy 1): with Anna's loan on book 15, the six overdue loans of REP-02.
    // Ruben's is past the 14 days after which the line manager is copied on the overdue notice.
    private static readonly (Guid Member, int Book, int DaysPastDue)[] OverdueLoans =
    [
        (Members.TigranMelikyan, 2, 3), (Members.RubenHarutyunyan, 9, 16), (Members.SamvelPoghosyan, 17, 8),
        (Members.KarenAsatryan, 22, 2), (Members.ErikZakaryan, 30, 5),
    ];

    // Open loans issued recently (book, copy 1): each is due at least 12 days after seeding (intern 14 days: at most
    // 2 days ago; employee 21: 9; manager 30: 18), so none of them turns overdue before the demo.
    private static readonly (Guid Member, int Book, int DaysAgo)[] RecentLoans =
    [
        (Members.NarekSimonyan, 8, 10), (Members.EvaHovsepyan, 10, 1), (Members.ManeGevorgyan, 21, 15),
        (Members.LusineKarapetyan, 23, 4), (Members.AnahitMartirosyan, 36, 6), (Members.AniSahakyan, 40, 3),
        (Members.LevonKhachatryan, 50, 12), (Members.ArturMinasyan, 51, 7), (Members.AshotBaghdasaryan, 52, 2),
    ];

    // Loans returned before their due date (book, copy 1): history for the reports, no penalty.
    private static readonly (Guid Member, int Book, int DaysAgo, int DaysEarly)[] OnTimeReturns =
    [
        (Members.VahanStepanyan, 3, 60, 2), (Members.LianaNalbandyan, 4, 75, 1), (Members.NuneTorosyan, 7, 90, 3),
        (Members.MilenaAbrahamyan, 11, 45, 5), (Members.AlinaMirzoyan, 13, 100, 2), (Members.SofiaHambardzumyan, 14, 55, 4),
        (Members.ElenDanielyan, 18, 120, 6), (Members.ArmenBabayan, 19, 65, 1), (Members.MariamAvetisyan, 20, 140, 10),
        (Members.AnnaPetrosyan, 24, 110, 3), (Members.NarekSimonyan, 25, 85, 8), (Members.AniSahakyan, 28, 70, 2),
        (Members.LilitGrigoryan, 29, 130, 5), (Members.AramSargsyan, 31, 95, 7), (Members.ManeGevorgyan, 33, 150, 12),
    ];

    // Copies that become OnLoan, Lost or Reserved: they must still be Available.
    private static readonly (int Book, int Copy)[] CopiesToLend =
    [
        (1, 1), (1, 2), (1, 3), (15, 1), (15, 2), (15, 3), (15, 4),
        .. OverdueLoans.Select(l => (l.Book, 1)), .. RecentLoans.Select(l => (l.Book, 1)),
    ];

    // Copies that only carry closed loans.
    private static readonly (int Book, int Copy)[] HistoryCopies =
        [(16, 1), (16, 2), (16, 3), .. OnTimeReturns.Select(l => (l.Book, 1))];

    private static readonly Guid[] AllMembers =
    [
        .. StaffMembers, .. OverdueLoans.Select(l => l.Member), .. RecentLoans.Select(l => l.Member),
        .. OnTimeReturns.Select(l => l.Member),
    ];

    public int Order => 30;

    public string Module => "Borrowing";

    public async Task SeedAsync(CancellationToken ct)
    {
        if (await db.Loans.AnyAsync(l => l.Id == Ids.Loan(1), ct))
            return;

        if (!await ReferencesAreReadyAsync(ct))
            return;

        DateTime now = clock.UtcNow;

        // Book 1: every copy on loan, two people waiting (BR-BOR-3: a reservation only when no copy is Available).
        await IssueAsync(1, (1, 1), Members.AnnaPetrosyan, now.AddDays(-5), ct);
        await IssueAsync(2, (1, 2), Members.MariamAvetisyan, now.AddDays(-3), ct);
        await IssueAsync(3, (1, 3), Members.DavitHakobyan, now.AddDays(-2), ct);
        db.Reservations.AddRange(
            Reservation.Create(Ids.Reservation(1), CatalogSeedIds.Book(1), Members.AramSargsyan, now.AddDays(-1), 1),
            Reservation.Create(Ids.Reservation(2), CatalogSeedIds.Book(1), Members.LilitGrigoryan, now.AddHours(-12), 2));

        // Book 15: Gor (Restricted) keeps his one loan; Anna's second loan is overdue; Sona lost a copy.
        await IssueAsync(4, (15, 1), Members.GorMkrtchyan, now.AddDays(-5), ct);
        int annaLoanDays = (await staff.GetLendingRulesAsync(Members.AnnaPetrosyan, ct)).LoanDays;
        Loan overdue = await IssueAsync(5, (15, 2), Members.AnnaPetrosyan, now.AddDays(-annaLoanDays - 6), ct);
        overdue.MarkOverdue(now);

        Loan lost = await IssueAsync(6, (15, 3), Members.SonaVardanyan, now.AddDays(-30), ct);
        lost.MarkLost(now.AddDays(-12));
        await catalog.SetCopyStatusAsync(CatalogSeedIds.Copy(15, 3), BookCopyStatus.Lost, ct);

        // The copy freed for the queue is held for Mariam for the pickup working days (BOR-07).
        var ready = Reservation.Create(Ids.Reservation(3), CatalogSeedIds.Book(15), Members.MariamAvetisyan, now.AddDays(-1), 1);
        ready.MarkReady(CatalogSeedIds.Copy(15, 4), ReservationQueueService.ReadyUntilUtc(options.Value.Reservations, calendar, clock));
        db.Reservations.Add(ready);
        await catalog.SetCopyStatusAsync(CatalogSeedIds.Copy(15, 4), BookCopyStatus.Reserved, ct);

        // Book 16: returned loans. Gor returned two books 3 working days late: 6 active points by default (Restricted).
        Loan gorLate1 = await LateReturnAsync(7, (16, 1), Members.GorMkrtchyan, now.AddDays(-50), ct);
        Loan gorLate2 = await LateReturnAsync(8, (16, 2), Members.GorMkrtchyan, now.AddDays(-45), ct);
        Loan annaLate = await LateReturnAsync(9, (16, 3), Members.AnnaPetrosyan, now.AddDays(-80), ct);
        // Davit's record must already be past its lifetime, whatever the setting: issued a lifetime and 50 days ago.
        Loan davitLate = await LateReturnAsync(10, (16, 3), Members.DavitHakobyan,
            now.AddDays(-options.Value.Penalties.LifetimeDays - 50), ct);
        Loan hayk = await IssueAsync(11, (16, 2), Members.HaykManukyan, now.AddDays(-200), ct, lend: false);
        hayk.Return(hayk.DueAtUtc.AddDays(-2));

        db.PenaltyRecords.AddRange(
            LatePenalty(1, gorLate1),
            LatePenalty(2, gorLate2),
            // Sona: the lost copy, 10 points by default — Suspended (penalty-point policy).
            PenaltyRecord.Create(Ids.Penalty(3), Members.SonaVardanyan, lost.Id, policy.CalculateLostPoints(),
                "Copy lost", lost.ReturnedAtUtc!.Value, policy.ExpiresAtUtc(lost.ReturnedAtUtc!.Value)));

        PenaltyRecord waived = LatePenalty(4, annaLate);
        waived.Waive("Late because of a business trip");
        PenaltyRecord expired = LatePenalty(5, davitLate);
        expired.Expire(now);
        db.PenaltyRecords.AddRange(waived, expired);

        // The other staff members: loans 12–40.
        int number = 11;
        foreach (var (member, book, daysPastDue) in OverdueLoans)
        {
            int loanDays = (await staff.GetLendingRulesAsync(member, ct)).LoanDays;
            Loan loan = await IssueAsync(++number, (book, 1), member, now.AddDays(-loanDays - daysPastDue), ct);
            loan.MarkOverdue(now);
        }

        foreach (var (member, book, daysAgo) in RecentLoans)
            await IssueAsync(++number, (book, 1), member, now.AddDays(-daysAgo), ct);

        foreach (var (member, book, daysAgo, daysEarly) in OnTimeReturns)
        {
            Loan loan = await IssueAsync(++number, (book, 1), member, now.AddDays(-daysAgo), ct, lend: false);
            loan.Return(loan.DueAtUtc.AddDays(-daysEarly));
        }

        logger.LogInformation("Borrowing seed: {Loans} loans, {Reservations} reservations, {Penalties} penalty records",
            Added<Loan>(), Added<Reservation>(), Added<PenaltyRecord>());
    }

    /// <summary>A loan of a seeded copy with the loan days of the staff member's category; the copy goes OnLoan.</summary>
    private async Task<Loan> IssueAsync(int number, (int Book, int Copy) copy, Guid staffMemberId, DateTime issuedAtUtc,
        CancellationToken ct, bool lend = true)
    {
        int loanDays = (await staff.GetLendingRulesAsync(staffMemberId, ct)).LoanDays;
        var loan = Loan.Issue(Ids.Loan(number), CatalogSeedIds.Copy(copy.Book, copy.Copy), staffMemberId, issuedAtUtc, loanDays);
        db.Loans.Add(loan);
        if (lend)
            await catalog.SetCopyStatusAsync(loan.BookCopyId, BookCopyStatus.OnLoan, ct);
        return loan;
    }

    /// <summary>A closed loan returned <see cref="LateWorkingDays"/> working days after its due date.</summary>
    private async Task<Loan> LateReturnAsync(int number, (int Book, int Copy) copy, Guid staffMemberId, DateTime issuedAtUtc,
        CancellationToken ct)
    {
        Loan loan = await IssueAsync(number, copy, staffMemberId, issuedAtUtc, ct, lend: false);
        DateOnly returnDay = calendar.AddWorkingDays(clock.ToLocalDate(loan.DueAtUtc), LateWorkingDays);
        loan.Return(clock.StartOfLocalDayUtc(returnDay).AddHours(12));
        return loan;
    }

    private int Added<T>() where T : class =>
        db.ChangeTracker.Entries<T>().Count(e => e.State == EntityState.Added);

    private PenaltyRecord LatePenalty(int number, Loan loan) =>
        PenaltyRecord.Create(Ids.Penalty(number), loan.StaffMemberId, loan.Id, policy.CalculateLatePoints(LateWorkingDays),
            $"Returned {LateWorkingDays} working days late", loan.ReturnedAtUtc!.Value,
            policy.ExpiresAtUtc(loan.ReturnedAtUtc!.Value));

    private async Task<bool> ReferencesAreReadyAsync(CancellationToken ct)
    {
        Guid[] memberIds = AllMembers.Distinct().ToArray();
        int members = await db.StaffMembers.CountAsync(m => memberIds.Contains(m.Id), ct);

        Guid[] lendIds = CopiesToLend.Select(c => CatalogSeedIds.Copy(c.Book, c.Copy)).ToArray();
        Guid[] historyIds = HistoryCopies.Select(c => CatalogSeedIds.Copy(c.Book, c.Copy)).ToArray();
        int available = await db.BookCopies.CountAsync(c => lendIds.Contains(c.Id) && c.Status == BookCopyStatus.Available, ct);
        int history = await db.BookCopies.CountAsync(c => historyIds.Contains(c.Id), ct);

        if (members == memberIds.Length && available == lendIds.Length && history == historyIds.Length)
            return true;

        logger.LogWarning(
            "Borrowing seed skipped: it needs {Members} seeded staff members, {Lend} Available seeded copies and {History} " +
            "history copies; found {FoundMembers}, {FoundLend} and {FoundHistory} (Staff and Catalog seed).",
            memberIds.Length, lendIds.Length, historyIds.Length, members, available, history);
        return false;
    }
}
