using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>Copy endpoints (CAT-04): any signed-in user reads, only Librarian and Administrator write (NFR-03).</summary>
public class BookCopyEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                factory.Account(role)).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    /// <summary>A book with one Available copy, written straight to the test database.</summary>
    private async Task<(Guid BookId, Guid CopyId)> CreateBookWithCopyAsync(string isbn, string barcode)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var category = new Category(Guid.NewGuid(), "Copies test category");
        var book = new Book(Guid.NewGuid(), "Copies test book", isbn, 2020, "en", null, category.Id, null);
        var copy = new BookCopy(Guid.NewGuid(), book.Id, barcode, BookCopyCondition.Good, "A-01-1");
        db.AddRange(category, book, copy);
        await db.SaveChangesAsync();
        return (book.Id, copy.Id);
    }

    [Theory]
    [InlineData(null, false, "9781000000900", "ROLE-01")]
    [InlineData(StaffRoles.StaffMember, false, "9781000000917", "ROLE-02")]
    [InlineData(StaffRoles.Librarian, true, "9781000000924", "ROLE-03")]
    [InlineData(StaffRoles.LibraryAdministrator, true, "9781000000931", "ROLE-04")]
    public async Task Copy_writes_require_librarian_or_administrator(string? role, bool allowed, string isbn, string barcode)
    {
        var (bookId, copyId) = await CreateBookWithCopyAsync(isbn, barcode);
        var denied = role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        using var client = Client(role);

        var add = await client.PostAsJsonAsync($"/api/books/{bookId}/copies",
            new { barcode = barcode + "-NEW", condition = "New", shelfLocation = "A-01-2" });
        Assert.Equal(allowed ? HttpStatusCode.Created : denied, add.StatusCode);

        var update = await client.PutAsJsonAsync($"/api/books/{bookId}/copies/{copyId}",
            new { condition = "Fair", shelfLocation = "B-02-1" });
        Assert.Equal(allowed ? HttpStatusCode.OK : denied, update.StatusCode);

        var status = await client.PostAsJsonAsync($"/api/books/{bookId}/copies/{copyId}/status",
            new { status = "UnderRepair" });
        Assert.Equal(allowed ? HttpStatusCode.OK : denied, status.StatusCode);

        // A refused request must not change anything.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Assert.Equal(allowed ? 2 : 1, await db.BookCopies.CountAsync(c => c.BookId == bookId));
        var copy = await db.BookCopies.SingleAsync(c => c.Id == copyId);
        Assert.Equal(allowed ? BookCopyStatus.UnderRepair : BookCopyStatus.Available, copy.Status);
        Assert.Equal(allowed ? BookCopyCondition.Fair : BookCopyCondition.Good, copy.Condition);
    }

    [Theory]
    [InlineData(StaffRoles.StaffMember, "9781000000948", "READ-01")]
    [InlineData(StaffRoles.Librarian, "9781000000955", "READ-02")]
    public async Task Any_signed_in_user_can_read_copies(string role, string isbn, string barcode)
    {
        var (bookId, copyId) = await CreateBookWithCopyAsync(isbn, barcode);
        using var client = Client(role);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/books/{bookId}/copies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/books/{bookId}/copies/{copyId}")).StatusCode);
    }

    [Fact]
    public async Task Reading_copies_without_a_token_is_401()
    {
        var (bookId, _) = await CreateBookWithCopyAsync("9781000000962", "READ-03");
        using var client = Client(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/books/{bookId}/copies")).StatusCode);
    }
}
