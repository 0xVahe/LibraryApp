using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibraryApp.Application.Abstractions.Catalog;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Events;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Messaging;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// A copy that becomes free outside a loan goes to the reservation queue (BOR-07, BQ-G1-10): Catalog's endpoints call
/// Borrowing's IReservationQueueService inside their own transaction. Real host and database; every test uses its own
/// book with one seeded copy.
/// </summary>
public class ReservationQueueEndpointTests(ApiHostWithoutBorrowingSeed factory) : IClassFixture<ApiHostWithoutBorrowingSeed>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private HttpClient Librarian()
    {
        var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
            factory.Account(StaffRoles.Librarian)).Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<T> Db<T>(Func<LibraryDbContext, Task<T>> read)
    {
        using var scope = factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<LibraryDbContext>());
    }

    /// <summary>The only seeded copy of the book gets this status, so nothing is Available and the book can be reserved.</summary>
    private Task SetSeededCopyAsync(int book, BookCopyStatus status) => Db(async db =>
    {
        var copy = await db.BookCopies.SingleAsync(c => c.Id == CatalogSeedIds.Copy(book, 1));
        copy.ChangeStatus(status);
        return await db.SaveChangesAsync();
    });

    private static async Task<ReservationDto> Reserve(HttpClient client, int book, Guid staffMemberId)
    {
        var response = await client.PostAsJsonAsync("/api/reservations", new CreateReservationRequest(CatalogSeedIds.Book(book), staffMemberId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReservationDto>(Json))!;
    }

    private static async Task<ReservationDto> Read(HttpClient client, Guid reservationId) =>
        (await client.GetFromJsonAsync<ReservationDto>($"/api/reservations/{reservationId}", Json))!;

    private static async Task<BookCopyDto> AddCopy(HttpClient client, int book, string barcode)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/books/{CatalogSeedIds.Book(book)}/copies", new AddCopyRequest(barcode, BookCopyCondition.New, "Q-1"), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BookCopyDto>(Json))!;
    }

    private Task<List<ReservationReady>> ReadyEventsAsync(Guid bookId) => Db(async db =>
        (await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.Type.Contains(nameof(ReservationReady))).ToListAsync())
            .Select(m => JsonSerializer.Deserialize<ReservationReady>(m.Payload, Json)!)
            .Where(e => e.BookId == bookId)
            .ToList());

    [Fact]
    public async Task A_new_copy_goes_to_the_first_in_the_queue_and_the_others_move_up()
    {
        await SetSeededCopyAsync(57, BookCopyStatus.OnLoan);
        using var librarian = Librarian();
        var first = await Reserve(librarian, 57, Members.MariamAvetisyan);
        var second = await Reserve(librarian, 57, Members.DavitHakobyan);

        var copy = await AddCopy(librarian, 57, "BK-0057-91");

        // The answer of Catalog already shows the copy held for the queue.
        Assert.Equal(BookCopyStatus.Reserved, copy.Status);

        var ready = await Read(librarian, first.ReservationId);
        Assert.Equal(ReservationStatus.ReadyForPickup, ready.Status);
        Assert.Equal(copy.Id, ready.CopyId);
        Assert.Equal(0, ready.QueuePosition);
        Assert.NotNull(ready.ReadyUntilUtc);
        Assert.True(ready.ReadyUntilUtc > DateTime.UtcNow.AddDays(2));   // 3 working days, at least 3 calendar days

        var waiting = await Read(librarian, second.ReservationId);
        Assert.Equal((ReservationStatus.Waiting, 1), (waiting.Status, waiting.QueuePosition));

        var readyEvent = Assert.Single(await ReadyEventsAsync(CatalogSeedIds.Book(57)));
        Assert.Equal(first.ReservationId, readyEvent.ReservationId);
        Assert.Equal(Members.MariamAvetisyan, readyEvent.StaffMemberId);
        Assert.Equal(ready.ReadyUntilUtc, readyEvent.ReadyUntilUtc);
    }

    [Fact]
    public async Task A_repaired_copy_goes_to_the_queue()
    {
        await SetSeededCopyAsync(27, BookCopyStatus.UnderRepair);
        using var librarian = Librarian();
        var reservation = await Reserve(librarian, 27, Members.AnnaPetrosyan);

        var response = await librarian.PostAsJsonAsync(
            $"/api/books/{CatalogSeedIds.Book(27)}/copies/{CatalogSeedIds.Copy(27, 1)}/status",
            new ChangeCopyStatusRequest(BookCopyStatus.Available), Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(BookCopyStatus.Reserved, (await response.Content.ReadFromJsonAsync<BookCopyDto>(Json))!.Status);
        var ready = await Read(librarian, reservation.ReservationId);
        Assert.Equal(ReservationStatus.ReadyForPickup, ready.Status);
        Assert.Equal(CatalogSeedIds.Copy(27, 1), ready.CopyId);
        Assert.Single(await ReadyEventsAsync(CatalogSeedIds.Book(27)));
    }

    [Fact]
    public async Task Without_a_queue_a_new_copy_stays_available()
    {
        using var librarian = Librarian();

        var copy = await AddCopy(librarian, 34, "BK-0034-91");

        Assert.Equal(BookCopyStatus.Available, copy.Status);
        Assert.Empty(await ReadyEventsAsync(CatalogSeedIds.Book(34)));
    }
}
