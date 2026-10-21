using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Borrowing.Services;
using LibraryApp.Application.Common;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Repositories.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// The Loan desk's list of copies held for pickup (BOR-07) on SQL Server, without the Borrowing seed, so the
/// class owns every reservation in its database.
/// </summary>
public class PickupListServiceTests(ApiHostWithoutBorrowingSeed factory) : IClassFixture<ApiHostWithoutBorrowingSeed>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private async Task<IReadOnlyList<Reservation>> SeedAsync()
    {
        // Three held copies with different deadlines (one already passed, not yet expired by the daily job), and one
        // reservation in each other state.
        Reservation Ready(int book, Guid member, double readyInDays)
        {
            var reservation = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(book), member, Now.AddDays(-5), 1);
            reservation.MarkReady(CatalogSeedIds.Copy(book, 1), Now.AddDays(readyInDays));
            return reservation;
        }

        var later = Ready(41, Members.AnnaPetrosyan, 3);
        var passed = Ready(42, Members.DavitHakobyan, -1);
        var soon = Ready(43, Members.MariamAvetisyan, 1);

        var waiting = Reservation.Create(Guid.NewGuid(), CatalogSeedIds.Book(44), Members.NarekSimonyan, Now.AddDays(-2), 1);
        var cancelled = Ready(45, Members.EvaHovsepyan, 2);
        cancelled.Cancel();
        var fulfilled = Ready(46, Members.TigranMelikyan, 2);
        fulfilled.Fulfil();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        db.Reservations.AddRange(later, passed, soon, waiting, cancelled, fulfilled);
        await db.SaveChangesAsync();

        return [passed, soon, later];
    }

    private static ReadyForPickupDto Row(Reservation r) =>
        new(r.Id, r.BookId, r.CopyId!.Value, r.StaffMemberId, r.ReadyUntilUtc!.Value);

    [Fact]
    public async Task Only_ready_reservations_are_listed_earliest_deadline_first_and_paged()
    {
        var expected = (await SeedAsync()).Select(Row).ToList();

        using var scope = factory.Services.CreateScope();
        var list = scope.ServiceProvider.GetRequiredService<IPickupListService>();
        Assert.IsType<PickupListService>(list);

        var all = await list.GetReadyForPickupAsync(new PageRequest());
        Assert.Equal(expected, all.Items);
        Assert.Equal(3, all.TotalCount);

        var second = await list.GetReadyForPickupAsync(new PageRequest(page: 2, pageSize: 2));
        Assert.Equal([expected[2]], second.Items);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(2, second.TotalPages);

        var pastTheEnd = await list.GetReadyForPickupAsync(new PageRequest(page: int.MaxValue, pageSize: 2));
        Assert.Empty(pastTheEnd.Items);
        Assert.Equal(3, pastTheEnd.TotalCount);
    }
}
