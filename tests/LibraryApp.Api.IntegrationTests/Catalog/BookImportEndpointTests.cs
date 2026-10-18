using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>CAT-09: POST /api/books/import with the real host and database.</summary>
public class BookImportEndpointTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    private const string Route = "/api/books/import";
    private const string Header = "Title,Isbn,PublicationYear,Language,Category,Authors,Publisher,Description";

    private HttpClient Client(string? role)
    {
        var client = factory.CreateClient();
        if (role is not null)
        {
            var token = factory.Services.GetRequiredService<IAccessTokenIssuer>()
                .Issue(new LoginAccount(Guid.NewGuid(), "import-tests@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active, [role]))
                .Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private static MultipartFormDataContent CsvFile(string csv)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { file, "file", "books.csv" } };
    }

    private static string NewIsbn()
    {
        var digits = "979" + Random.Shared.NextInt64(0, 1_000_000_000).ToString("D9");
        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (digits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return digits + (10 - sum % 10) % 10;
    }

    /// <summary>A category and an author with names no other test uses, so the name lookup is never ambiguous.</summary>
    private async Task<(string Category, string Author)> SeedNamesAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var category = new Category(Guid.NewGuid(), $"Import category {suffix}");
        var author = new Author(Guid.NewGuid(), $"Import author {suffix}");
        db.AddRange(category, author);
        await db.SaveChangesAsync();
        return (category.Name, author.FullName);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(StaffRoles.StaffMember, HttpStatusCode.Forbidden)]
    public async Task Only_librarians_and_administrators_import(string? role, HttpStatusCode expected)
    {
        using var client = Client(role);

        using var response = await client.PostAsync(Route, CsvFile(Header + "\n"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task A_valid_file_creates_every_book(string role)
    {
        var (category, author) = await SeedNamesAsync();
        var first = NewIsbn();
        var second = NewIsbn();
        var csv = Header + "\n" +
                  $"Imported One,{first},2020,en,{category},{author},,\n" +
                  $"\"Imported, Two\",{second},2021,hy,{category},{author},,\"With, comma\"\n";
        using var client = Client(role);

        using var response = await client.PostAsync(Route, CsvFile(csv));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BookImportResult>())!;
        Assert.Equal(2, result.CreatedCount);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Assert.Equal(2, await db.Books.CountAsync(x => x.Isbn == first || x.Isbn == second));
    }

    [Fact]
    public async Task An_invalid_row_saves_nothing_and_answers_400_with_every_row_problem()
    {
        var (category, author) = await SeedNamesAsync();
        var valid = NewIsbn();
        var csv = Header + "\n" +
                  $"Good,{valid},2020,en,{category},{author},,\n" +
                  $"Bad,{NewIsbn()},2020,en,No Such Category,{author},,\n";
        using var client = Client(StaffRoles.Librarian);

        using var response = await client.PostAsync(Route, CsvFile(csv));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("catalog.import_invalid", body.GetProperty("code").GetString());
        var row = Assert.Single(body.GetProperty("rows").EnumerateArray());
        Assert.Equal(3, row.GetProperty("rowNumber").GetInt32());
        Assert.Equal("Category", row.GetProperty("field").GetString());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        Assert.False(await db.Books.AnyAsync(x => x.Isbn == valid));
    }

    [Theory]
    [InlineData("")]
    [InlineData(Header + "\n")]
    [InlineData("Title,Isbn\nA,9791000000015\n")]
    public async Task An_empty_file_a_file_without_rows_or_a_broken_header_is_400(string csv)
    {
        using var client = Client(StaffRoles.Librarian);

        using var response = await client.PostAsync(Route, CsvFile(csv));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_shows_a_file_picker_for_the_import()
    {
        using var client = factory.CreateClient();

        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        var body = swagger.GetProperty("paths").GetProperty(Route).GetProperty("post").GetProperty("requestBody");
        var schema = body.GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema");
        var file = schema.GetProperty("properties").GetProperty("file");
        Assert.Equal("binary", file.GetProperty("format").GetString());
    }
}
