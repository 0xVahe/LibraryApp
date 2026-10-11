using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Borrowing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Members = LibraryApp.Infrastructure.Persistence.Seed.Staff.StaffSeedIds.StaffMembers;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>GET /api/books/{id} (CAT-06): any signed-in user sees the book with "N of M available".</summary>
public class BookDetailsEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                new LoginAccount(Guid.NewGuid(), "demo@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active, [role])).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    /// <summary>A book with 3 copies, 1 of them OnLoan (the acceptance criterion of CAT-06).</summary>
    private async Task<Guid> CreateBookAsync(string isbn, string barcodePrefix, bool deleted = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var category = new Category(Guid.NewGuid(), "Details test category");
        var author = new Author(Guid.NewGuid(), "Robert C. Martin");
        var book = new Book(Guid.NewGuid(), "Clean Code", isbn, 2008, "en", null, category.Id, null);
        if (deleted)
            book.Delete(DateTime.UtcNow);

        var onLoan = new BookCopy(Guid.NewGuid(), book.Id, barcodePrefix + "-3", BookCopyCondition.Good, null);
        onLoan.ChangeStatus(BookCopyStatus.OnLoan);

        db.AddRange(category, author, book, new BookAuthor(book.Id, author.Id),
            new BookCopy(Guid.NewGuid(), book.Id, barcodePrefix + "-1", BookCopyCondition.Good, null),
            new BookCopy(Guid.NewGuid(), book.Id, barcodePrefix + "-2", BookCopyCondition.Good, null),
            onLoan);
        await db.SaveChangesAsync();
        return book.Id;
    }

    [Theory]
    [InlineData(StaffRoles.StaffMember, "9781000000979", "DET-1")]
    [InlineData(StaffRoles.Librarian, "9781000000986", "DET-2")]
    public async Task A_signed_in_user_sees_2_of_3_available(string role, string isbn, string barcodePrefix)
    {
        var bookId = await CreateBookAsync(isbn, barcodePrefix);
        using var client = Client(role);

        var response = await client.GetAsync($"/api/books/{bookId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var book = await response.Content.ReadFromJsonAsync<BookDetailsDto>();
        Assert.NotNull(book);
        Assert.Equal("Clean Code", book.Title);
        Assert.Equal(new[] { "Robert C. Martin" }, book.Authors);
        Assert.Equal("Details test category", book.CategoryName);
        Assert.Equal(3, book.TotalCopies);
        Assert.Equal(2, book.AvailableCopies);
        Assert.Equal("2 of 3 available", book.Availability);
        Assert.Null(book.NextExpectedReturnDate);
    }

    [Fact]
    public async Task A_book_with_a_copy_on_loan_shows_the_next_expected_return_date()
    {
        DateTime issuedAtUtc = DateTime.UtcNow;
        var loan = Loan.Issue(Guid.NewGuid(), CatalogSeedIds.Copy(1, 1), Members.AnnaPetrosyan, issuedAtUtc, 7);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            db.Loans.Add(loan);
            await db.SaveChangesAsync();
        }

        using var client = Client(StaffRoles.StaffMember);
        var response = await client.GetAsync($"/api/books/{CatalogSeedIds.Book(1)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var book = await response.Content.ReadFromJsonAsync<BookDetailsDto>();
        Assert.NotNull(book);
        var clock = factory.Services.GetRequiredService<IClock>();
        Assert.Equal(clock.ToLocalDate(loan.DueAtUtc), book.NextExpectedReturnDate);
    }

    [Fact]
    public async Task An_unknown_book_is_404_with_the_catalog_code()
    {
        using var client = Client(StaffRoles.StaffMember);

        var response = await client.GetAsync($"/api/books/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("catalog.book_not_found", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_deleted_book_is_404()
    {
        var bookId = await CreateBookAsync("9781000000993", "DET-3", deleted: true);
        using var client = Client(StaffRoles.Librarian);

        var response = await client.GetAsync($"/api/books/{bookId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        var bookId = await CreateBookAsync("9781000001006", "DET-4");
        using var client = Client(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/books/{bookId}")).StatusCode);
    }
}
