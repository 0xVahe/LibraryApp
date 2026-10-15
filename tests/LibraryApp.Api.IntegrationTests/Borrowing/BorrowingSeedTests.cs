using LibraryApp.Application.Abstractions.Borrowing;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ids = LibraryApp.Infrastructure.Persistence.Seed.Borrowing.BorrowingSeedIds;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>The Borrowing seed (SD-17) on the real host in Development, after the Catalog and Staff seed.</summary>
public class BorrowingSeedTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private async Task<T> Read<T>(Func<IServiceProvider, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider);
    }

    private static LibraryDbContext Db(IServiceProvider services) => services.GetRequiredService<LibraryDbContext>();

    private static readonly Guid[] LoanIds = Enumerable.Range(1, 11).Select(Ids.Loan).ToArray();
    private static readonly Guid[] ReservationIds = Enumerable.Range(1, 3).Select(Ids.Reservation).ToArray();
    private static readonly Guid[] PenaltyIds = Enumerable.Range(1, 5).Select(Ids.Penalty).ToArray();

    private Task<IReadOnlyList<OpenLoanInfo>> OpenLoans(Guid member) =>
        Read(s => s.GetRequiredService<IBorrowingReadService>().GetActiveLoansAsync(member));

    private Task<BookCopyStatus> CopyStatus(int book, int copy) =>
        Read(s => Db(s).BookCopies.Where(c => c.Id == CatalogSeedIds.Copy(book, copy)).Select(c => c.Status).SingleAsync());

    [Fact]
    public async Task Loans_reservations_and_penalties_are_seeded_with_constant_ids()
    {
        var loans = await Read(s => Db(s).Loans.AsNoTracking().Where(l => LoanIds.Contains(l.Id)).ToListAsync());
        var reservations = await Read(s => Db(s).Reservations.CountAsync(r => ReservationIds.Contains(r.Id)));
        var penalties = await Read(s => Db(s).PenaltyRecords.AsNoTracking().Where(p => PenaltyIds.Contains(p.Id)).ToListAsync());

        Assert.Equal(LoanIds.Length, loans.Count);
        Assert.Equal(4, loans.Count(l => l.Status == LoanStatus.Active));
        Assert.Single(loans, l => l.Status == LoanStatus.Overdue);
        Assert.Single(loans, l => l.Status == LoanStatus.Lost);
        Assert.Equal(5, loans.Count(l => l.Status == LoanStatus.Returned));
        Assert.Equal(ReservationIds.Length, reservations);
        Assert.Equal(3, penalties.Count(p => p.Status == PenaltyRecordStatus.Active));
        Assert.Single(penalties, p => p.Status == PenaltyRecordStatus.Waived);
        Assert.Single(penalties, p => p.Status == PenaltyRecordStatus.Expired);
    }

    [Theory]
    [InlineData(1, 1, BookCopyStatus.OnLoan)]
    [InlineData(1, 2, BookCopyStatus.OnLoan)]
    [InlineData(1, 3, BookCopyStatus.OnLoan)]
    [InlineData(15, 1, BookCopyStatus.OnLoan)]
    [InlineData(15, 2, BookCopyStatus.OnLoan)]
    [InlineData(15, 3, BookCopyStatus.Lost)]
    [InlineData(15, 4, BookCopyStatus.Reserved)]
    [InlineData(16, 1, BookCopyStatus.Available)]
    [InlineData(16, 2, BookCopyStatus.Available)]
    [InlineData(16, 3, BookCopyStatus.Available)]
    public async Task Copy_statuses_match_the_loans_and_the_held_copy(int book, int copy, BookCopyStatus expected)
    {
        Assert.Equal(expected, await CopyStatus(book, copy));
    }

    [Fact]
    public async Task Penalty_points_match_the_staff_statuses_of_the_staff_seed()
    {
        var policy = factory.Services.GetRequiredService<PenaltyPolicy>();
        foreach (var (member, points) in new[] { (Members.GorMkrtchyan, 6), (Members.SonaVardanyan, 10), (Members.AnnaPetrosyan, 0), (Members.DavitHakobyan, 0) })
        {
            var summary = await Read(s => s.GetRequiredService<IBorrowingReadService>().GetStaffSummaryAsync(member));
            var status = await Read(s => Db(s).StaffMembers.Where(m => m.Id == member).Select(m => m.Status).SingleAsync());

            Assert.Equal(points, summary.ActivePenaltyPoints);   // waived (Anna) and expired (Davit) do not count
            Assert.Equal(status, policy.GetStatus(points));
        }
    }

    [Fact]
    public async Task Open_loans_follow_the_lending_rules_and_BR_MEM_4()
    {
        Assert.Equal(2, (await OpenLoans(Members.AnnaPetrosyan)).Count);   // Employee: up to 3
        Assert.Single(await OpenLoans(Members.GorMkrtchyan));              // Restricted: 1 (BR-MEM-3)
        Assert.Empty(await OpenLoans(Members.HaykManukyan));               // Deactivated: none (BR-MEM-4)
        Assert.Empty(await OpenLoans(Members.SonaVardanyan));              // the lost loan is closed
    }

    [Fact]
    public async Task The_queue_waits_for_book_1_and_a_copy_of_book_15_is_held_for_pickup()
    {
        var reservations = await Read(s => Db(s).Reservations.AsNoTracking()
            .Where(r => ReservationIds.Contains(r.Id)).ToListAsync());
        var now = await Read(s => Task.FromResult(s.GetRequiredService<IClock>().UtcNow));

        var waiting = reservations.Where(r => r.Status == ReservationStatus.Waiting).OrderBy(r => r.QueuePosition).ToList();
        Assert.Equal([Members.AramSargsyan, Members.LilitGrigoryan], waiting.Select(r => r.StaffMemberId));
        Assert.All(waiting, r => Assert.Equal(CatalogSeedIds.Book(1), r.BookId));
        Assert.Equal([1, 2], waiting.Select(r => r.QueuePosition));

        var ready = Assert.Single(reservations, r => r.Status == ReservationStatus.ReadyForPickup);
        Assert.Equal(Members.MariamAvetisyan, ready.StaffMemberId);
        Assert.Equal(CatalogSeedIds.Copy(15, 4), ready.CopyId);
        Assert.True(ready.ReadyUntilUtc > now);
    }

    [Fact]
    public async Task A_second_run_adds_nothing()
    {
        int Count(LibraryDbContext db) => db.Loans.Count() + db.Reservations.Count() + db.PenaltyRecords.Count();
        int before = await Read(s => Task.FromResult(Count(Db(s))));

        using (var scope = factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetServices<IModuleSeeder>().OfType<BorrowingSeeder>().Single();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(seeder.SeedAsync);
        }

        Assert.Equal(before, await Read(s => Task.FromResult(Count(Db(s)))));
    }
}
