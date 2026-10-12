using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

public class BookCrudEndpointTests(ApiHostFactory factory)
    : IClassFixture<ApiHostFactory>
{
    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();

        if (role is not null)
        {
            var token = factory.Services
                .GetRequiredService<IAccessTokenIssuer>()
                .Issue(new LoginAccount(
                    Guid.NewGuid(),
                    "demo@ameriabank.am",
                    Guid.NewGuid(),
                    StaffMemberStatus.Active,
                    [role]))
                .Token;

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static string NewIsbn()
    {
        var firstTwelveDigits = "978" +
            Random.Shared.NextInt64(0, 1_000_000_000)
                .ToString("D9");

        var sum = 0;

        for (var i = 0; i < 12; i++)
        {
            sum += (firstTwelveDigits[i] - '0') *
                   (i % 2 == 0 ? 1 : 3);
        }

        var checkDigit = (10 - sum % 10) % 10;

        return firstTwelveDigits + checkDigit;
    }

    private static CreateBookRequest CreateRequest(
        Guid categoryId,
        Guid authorId) =>
        new(
            "Integration Test Book",
            NewIsbn(),
            2024,
            "en",
            "Created by an integration test",
            categoryId,
            null,
            [authorId]);

    private static UpdateBookRequest UpdateRequest(
        Guid categoryId) =>
        new(
            "Updated Integration Test Book",
            NewIsbn(),
            2023,
            "fr",
            "Updated by an integration test",
            categoryId,
            null);

    private async Task<(Guid BookId, Guid CategoryId)> SeedBookAsync(
        string? isbn = null,
        bool withOnLoanCopy = false,
        bool withReservedCopy = false,
        bool withAvailableCopy = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var category = new Category(Guid.NewGuid(), "CRUD test category");
        var author = new Author(Guid.NewGuid(), "CRUD test author");

        var book = new Book(
            Guid.NewGuid(),
            "Existing Integration Test Book",
            isbn is null
                ? NewIsbn()
                : isbn.Replace("-", "").Replace(" ", ""),
            2022,
            "en",
            null,
            category.Id,
            null);

        db.AddRange(category, author, book,
            new BookAuthor(book.Id, author.Id));

        if (withOnLoanCopy || withReservedCopy || withAvailableCopy)
        {
            var copy = new BookCopy(
                Guid.NewGuid(),
                book.Id,
                $"C{Guid.NewGuid():N}"[..12],
                BookCopyCondition.Good,
                null);

            if (withReservedCopy)
                copy.ChangeStatus(BookCopyStatus.Reserved);
            else if (withOnLoanCopy)
                copy.ChangeStatus(BookCopyStatus.OnLoan);

            db.Add(copy);
        }

        await db.SaveChangesAsync();

        return (book.Id, category.Id);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.Librarian, HttpStatusCode.Created)]
    [InlineData(StaffRoles.LibraryAdministrator, HttpStatusCode.Created)]
    public async Task Creating_a_book_enforces_roles(
        string? role,
        HttpStatusCode expected)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var category = new Category(Guid.NewGuid(), "Create role test category");
        var author = new Author(Guid.NewGuid(), "Create role test author");

        db.AddRange(category, author);
        await db.SaveChangesAsync();

        using var client = Client(role);

        var response = await client.PostAsJsonAsync(
            "/api/books",
            CreateRequest(category.Id, author.Id));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_valid_book_returns_201_and_persists_it()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var category = new Category(Guid.NewGuid(), "Create test category");
        var author = new Author(Guid.NewGuid(), "Create test author");

        db.AddRange(category, author);
        await db.SaveChangesAsync();

        var request = CreateRequest(category.Id, author.Id);

        using var client = Client(StaffRoles.Librarian);
        var response = await client.PostAsJsonAsync("/api/books", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var id = await response.Content.ReadFromJsonAsync<Guid>();

        Assert.NotEqual(Guid.Empty, id);

        db.ChangeTracker.Clear();

        var savedBook = await db.Books
            .SingleAsync(x => x.Id == id);

        Assert.Equal(request.Title, savedBook.Title);
        Assert.Equal(request.Isbn, savedBook.Isbn);
        Assert.False(savedBook.IsDeleted);

        Assert.True(await db.BookAuthors.AnyAsync(
            x => x.BookId == id && x.AuthorId == author.Id));
    }

    [Fact]
    public async Task Creating_a_book_with_duplicate_isbn_returns_409()
    {
        const string duplicateIsbn = "978-0-13-468599-1";

        var (_, categoryId) = await SeedBookAsync(
            isbn: duplicateIsbn);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var author = new Author(Guid.NewGuid(), "Duplicate ISBN test author");
        db.Add(author);
        await db.SaveChangesAsync();

        var request = CreateRequest(categoryId, author.Id) with
        {
            Isbn = duplicateIsbn
        };

        using var client = Client(StaffRoles.Librarian);

        var response = await client.PostAsJsonAsync("/api/books", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            "catalog.book_isbn_duplicate",
            problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deleting_a_book_with_a_reserved_copy_returns_409()
    {
        var (bookId, _) = await SeedBookAsync(
            withReservedCopy: true);

        using var client = Client(StaffRoles.Librarian);

        var response = await client.DeleteAsync($"/api/books/{bookId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        db.ChangeTracker.Clear();

        var book = await db.Books.SingleAsync(x => x.Id == bookId);

        Assert.False(book.IsDeleted);
    }

    [Fact]
    public async Task Invalid_isbn_returns_400_validation_problem()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var category = new Category(Guid.NewGuid(), "Validation test category");
        var author = new Author(Guid.NewGuid(), "Validation test author");

        db.AddRange(category, author);
        await db.SaveChangesAsync();

        var request = CreateRequest(category.Id, author.Id) with
        {
            Isbn = "not-an-isbn"
        };

        using var client = Client(StaffRoles.Librarian);
        var response = await client.PostAsJsonAsync("/api/books", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "validation_failed",
            problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Creating_a_book_without_authors_returns_400()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var category = new Category(Guid.NewGuid(), "No author test category");
        db.Add(category);
        await db.SaveChangesAsync();

        var request = CreateRequest(category.Id, Guid.NewGuid()) with
        {
            AuthorIds = []
        };

        using var client = Client(StaffRoles.Librarian);
        var response = await client.PostAsJsonAsync("/api/books", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.Librarian, HttpStatusCode.NoContent)]
    [InlineData(StaffRoles.LibraryAdministrator, HttpStatusCode.NoContent)]
    public async Task Updating_a_book_enforces_roles(
        string? role,
        HttpStatusCode expected)
    {
        var (bookId, categoryId) = await SeedBookAsync();

        using var client = Client(role);

        var response = await client.PutAsJsonAsync(
            $"/api/books/{bookId}",
            UpdateRequest(categoryId));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Updating_a_book_persists_the_changes()
    {
        var (bookId, categoryId) = await SeedBookAsync();
        var request = UpdateRequest(categoryId);

        using var client = Client(StaffRoles.Librarian);

        var response = await client.PutAsJsonAsync(
            $"/api/books/{bookId}",
            request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        db.ChangeTracker.Clear();

        var updatedBook = await db.Books
            .SingleAsync(x => x.Id == bookId);

        Assert.Equal(request.Title, updatedBook.Title);
        Assert.Equal(request.Isbn, updatedBook.Isbn);
        Assert.Equal("fr", updatedBook.Language);
        Assert.False(updatedBook.IsDeleted);
    }

    [Fact]
    public async Task Updating_a_missing_book_returns_404()
    {
        var (_, categoryId) = await SeedBookAsync();

        using var client = Client(StaffRoles.Librarian);

        var response = await client.PutAsJsonAsync(
            $"/api/books/{Guid.NewGuid()}",
            UpdateRequest(categoryId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    [InlineData(StaffRoles.Librarian, HttpStatusCode.NoContent)]
    [InlineData(StaffRoles.LibraryAdministrator, HttpStatusCode.NoContent)]
    public async Task Deleting_a_book_enforces_roles(
        string? role,
        HttpStatusCode expected)
    {
        var (bookId, _) = await SeedBookAsync();

        using var client = Client(role);

        var response = await client.DeleteAsync($"/api/books/{bookId}");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_book_soft_deletes_it()
    {
        var (bookId, _) = await SeedBookAsync();

        using var client = Client(StaffRoles.Librarian);

        var response = await client.DeleteAsync($"/api/books/{bookId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        db.ChangeTracker.Clear();

        var deletedBook = await db.Books
            .SingleAsync(x => x.Id == bookId);

        Assert.True(deletedBook.IsDeleted);
        Assert.NotNull(deletedBook.DeletedAtUtc);
    }

    [Fact]
    public async Task Deleting_a_book_with_an_available_copy_withdraws_the_copy()
    {
        var (bookId, _) = await SeedBookAsync(withAvailableCopy: true);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        var copyBeforeDelete = await db.BookCopies
            .SingleAsync(x => x.BookId == bookId);

        Assert.Equal(BookCopyStatus.Available, copyBeforeDelete.Status);

        using var client = Client(StaffRoles.Librarian);

        var response = await client.DeleteAsync($"/api/books/{bookId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        db.ChangeTracker.Clear();

        var deletedBook = await db.Books
            .SingleAsync(x => x.Id == bookId);

        var withdrawnCopy = await db.BookCopies
            .SingleAsync(x => x.BookId == bookId);

        Assert.True(deletedBook.IsDeleted);
        Assert.Equal(BookCopyStatus.Withdrawn, withdrawnCopy.Status);
    }

    [Fact]
    public async Task Deleting_a_book_with_a_copy_on_loan_returns_409()
    {
        var (bookId, _) = await SeedBookAsync(withOnLoanCopy: true);

        using var client = Client(StaffRoles.Librarian);

        var response = await client.DeleteAsync($"/api/books/{bookId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();

        db.ChangeTracker.Clear();

        var book = await db.Books.SingleAsync(x => x.Id == bookId);

        Assert.False(book.IsDeleted);
    }

    [Fact]
    public async Task Deleting_a_missing_book_returns_404()
    {
        using var client = Client(StaffRoles.Librarian);

        var response = await client.DeleteAsync(
            $"/api/books/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
