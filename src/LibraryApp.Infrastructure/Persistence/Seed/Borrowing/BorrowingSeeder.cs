using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Abstractions.Staff;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ids = LibraryApp.Infrastructure.Persistence.Seed.Borrowing.BorrowingSeedIds;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Infrastructure.Persistence.Seed.Borrowing;

/// <summary>
/// Demo data of Borrowing (SD-17, Development only, synthetic C-2): loans in every status, a waiting queue, a copy held
/// for pickup and penalty records in every status, matching the statuses of the Staff seed (Gor Restricted with 6 active
/// points, Sona Suspended with 10, Hayk Deactivated without open loans).
/// <list type="bullet">
/// <item>Book 1 (3 copies): all on loan (Anna, Mariam, Davit); Aram and Lilit wait in the queue.</item>
/// <item>Book 15 (4 copies): Gor's active loan, Anna's overdue loan, Sona's lost copy, the copy held for Mariam.</item>
/// <item>Book 16 (3 copies): returned loans only — the history behind the penalties.</item>
/// </list>
/// Dates are relative to <see cref="IClock.UtcNow"/>; late returns are counted in working days (BR-BOR-5). Entities are
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
    ILogger<BorrowingSeeder> logger) : IModuleSeeder
{
    private const int PointsPerLateReturn = 3;

    private static readonly Guid[] StaffMembers =
    [
        Members.AnnaPetrosyan, Members.MariamAvetisyan, Members.DavitHakobyan, Members.AramSargsyan,
        Members.LilitGrigoryan, Members.GorMkrtchyan, Members.SonaVardanyan, Members.HaykManukyan,
    ];

    // Copies that become OnLoan, Lost or Reserved: they must still be Available.
    private static readonly (int Book, int Copy)[] CopiesToLend = [(1, 1), (1, 2), (1, 3), (15, 1), (15, 2), (15, 3), (15, 4)];

    // Copies that only carry closed loans.
    private static readonly (int Book, int Copy)[] HistoryCopies = [(16, 1), (16, 2), (16, 3)];

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

        // The copy freed for the queue is held for Mariam for 3 working days (BOR-07).
        var ready = Reservation.Create(Ids.Reservation(3), CatalogSeedIds.Book(15), Members.MariamAvetisyan, now.AddDays(-1), 1);
        ready.MarkReady(CatalogSeedIds.Copy(15, 4), clock.EndOfLocalDayUtc(calendar.AddWorkingDays(clock.LocalToday, 3)));
        db.Reservations.Add(ready);
        await catalog.SetCopyStatusAsync(CatalogSeedIds.Copy(15, 4), BookCopyStatus.Reserved, ct);

        // Book 16: returned loans. Gor returned two books 3 working days late: 6 active points (Restricted).
        Loan gorLate1 = await LateReturnAsync(7, (16, 1), Members.GorMkrtchyan, now.AddDays(-50), ct);
        Loan gorLate2 = await LateReturnAsync(8, (16, 2), Members.GorMkrtchyan, now.AddDays(-45), ct);
        Loan annaLate = await LateReturnAsync(9, (16, 3), Members.AnnaPetrosyan, now.AddDays(-80), ct);
        Loan davitLate = await LateReturnAsync(10, (16, 3), Members.DavitHakobyan, now.AddDays(-140), ct);
        Loan hayk = await IssueAsync(11, (16, 2), Members.HaykManukyan, now.AddDays(-200), ct, lend: false);
        hayk.Return(hayk.DueAtUtc.AddDays(-2));

        db.PenaltyRecords.AddRange(
            LatePenalty(1, gorLate1),
            LatePenalty(2, gorLate2),
            // Sona: the lost copy, 10 points — Suspended for 30 days (BR-BOR-6).
            PenaltyRecord.Create(Ids.Penalty(3), Members.SonaVardanyan, lost.Id, PenaltyPolicy.LostPoints,
                "Copy lost", lost.ReturnedAtUtc!.Value));

        PenaltyRecord waived = LatePenalty(4, annaLate);
        waived.Waive("Late because of a business trip");
        PenaltyRecord expired = LatePenalty(5, davitLate);
        expired.Expire(now);
        db.PenaltyRecords.AddRange(waived, expired);

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

    /// <summary>A closed loan returned <see cref="PointsPerLateReturn"/> working days after its due date.</summary>
    private async Task<Loan> LateReturnAsync(int number, (int Book, int Copy) copy, Guid staffMemberId, DateTime issuedAtUtc,
        CancellationToken ct)
    {
        Loan loan = await IssueAsync(number, copy, staffMemberId, issuedAtUtc, ct, lend: false);
        DateOnly returnDay = calendar.AddWorkingDays(clock.ToLocalDate(loan.DueAtUtc), PointsPerLateReturn);
        loan.Return(clock.StartOfLocalDayUtc(returnDay).AddHours(12));
        return loan;
    }

    private int Added<T>() where T : class =>
        db.ChangeTracker.Entries<T>().Count(e => e.State == EntityState.Added);

    private static PenaltyRecord LatePenalty(int number, Loan loan) =>
        PenaltyRecord.Create(Ids.Penalty(number), loan.StaffMemberId, loan.Id, PointsPerLateReturn,
            $"Returned {PointsPerLateReturn} working days late", loan.ReturnedAtUtc!.Value);

    private async Task<bool> ReferencesAreReadyAsync(CancellationToken ct)
    {
        int members = await db.StaffMembers.CountAsync(m => StaffMembers.Contains(m.Id), ct);

        Guid[] lendIds = CopiesToLend.Select(c => CatalogSeedIds.Copy(c.Book, c.Copy)).ToArray();
        Guid[] historyIds = HistoryCopies.Select(c => CatalogSeedIds.Copy(c.Book, c.Copy)).ToArray();
        int available = await db.BookCopies.CountAsync(c => lendIds.Contains(c.Id) && c.Status == BookCopyStatus.Available, ct);
        int history = await db.BookCopies.CountAsync(c => historyIds.Contains(c.Id), ct);

        if (members == StaffMembers.Length && available == lendIds.Length && history == historyIds.Length)
            return true;

        logger.LogWarning(
            "Borrowing seed skipped: it needs {Members} seeded staff members, {Lend} Available seeded copies and {History} " +
            "history copies; found {FoundMembers}, {FoundLend} and {FoundHistory} (Staff and Catalog seed).",
            StaffMembers.Length, lendIds.Length, historyIds.Length, members, available, history);
        return false;
    }
}
