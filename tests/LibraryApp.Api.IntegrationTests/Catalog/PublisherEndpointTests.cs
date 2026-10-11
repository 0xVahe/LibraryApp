using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LibraryApp.Api.IntegrationTests.Catalog;

/// <summary>Real routing, JWT, validation and error handling; only persistence is replaced.</summary>
public sealed class PublisherHostFactory : WebApplicationFactory<Program>
{
    internal void AddBookReference(Guid publisherId, bool deleted)
    {
        var book = new Book(Guid.NewGuid(), "Synthetic book", "9780134685991", 2020, "en", null, Guid.NewGuid(), publisherId);
        if (deleted) book.Delete(DateTime.UtcNow);
        Services.GetRequiredService<PublisherTestStore>().Books.Add(book);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:LibraryDb", "Server=localhost;Database=UnusedPublisherTests;Integrated Security=true;TrustServerCertificate=true");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Seed:Enabled", "false");
        builder.UseSetting("UseHttpsRedirection", "false");
        builder.UseSetting("Messaging:DispatcherEnabled", "false");
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<PublisherTestStore>();
            services.Replace(ServiceDescriptor.Singleton<IPublisherRepository>(sp => sp.GetRequiredService<PublisherTestStore>()));
            services.Replace(ServiceDescriptor.Singleton<IUnitOfWork>(sp => sp.GetRequiredService<PublisherTestStore>()));
            services.Replace(ServiceDescriptor.Singleton<IIdGenerator>(sp => sp.GetRequiredService<PublisherTestStore>()));
            services.Replace(ServiceDescriptor.Singleton<IApplicationLock>(sp => sp.GetRequiredService<PublisherTestStore>()));
        });
    }

    private sealed class PublisherTestStore : IPublisherRepository, IUnitOfWork, IIdGenerator, IApplicationLock
    {
        private readonly List<Publisher> _publishers = [];
        public List<Book> Books { get; } = [];
        public Guid NewId() => Guid.NewGuid();
        public Task AcquireAsync(string resource, CancellationToken ct = default) => Task.CompletedTask;
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<IReadOnlyList<PublisherDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PublisherDto>>(_publishers.Select(x => new PublisherDto(x.Id, x.Name)).ToArray());
        public Task<Publisher?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(_publishers.SingleOrDefault(x => x.Id == id));
        public void Add(Publisher publisher) => _publishers.Add(publisher);
        public void Remove(Publisher publisher) => _publishers.Remove(publisher);
        public Task<bool> IsInUseAsync(Guid id, CancellationToken ct) => Task.FromResult(Books.Any(x => x.PublisherId == id));
    }
}

public class PublisherEndpointTests(PublisherHostFactory factory)
    : PublisherEndpointChecks<PublisherHostFactory>(factory), IClassFixture<PublisherHostFactory>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Referenced_publishers_return_409_through_the_real_service_and_error_handler(bool deleted)
    {
        using var client = Client(StaffRoles.Librarian);
        var publisher = await Create(client);
        Factory.AddBookReference(publisher.Id, deleted);
        using var response = await client.DeleteAsync($"/api/publishers/{publisher.Id}");
        await AssertProblem(response, HttpStatusCode.Conflict, "catalog.publisher_in_use");
        Assert.Equal(publisher, await client.GetFromJsonAsync<PublisherDto>($"/api/publishers/{publisher.Id}"));
    }
}

public abstract class PublisherEndpointChecks<TFactory>(TFactory factory) where TFactory : WebApplicationFactory<Program>
{
    protected TFactory Factory { get; } = factory;

    protected HttpClient Client(string? role)
    {
        var client = Factory.CreateClient();
        if (role is not null)
        {
            var token = Factory.Services.GetRequiredService<IAccessTokenIssuer>().Issue(
                new LoginAccount(Guid.NewGuid(), "demo@ameriabank.am", Guid.NewGuid(), StaffMemberStatus.Active,
                    role.Length == 0 ? [] : [role])).Token;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    protected static async Task<PublisherDto> Create(HttpClient client, string name = "Synthetic Publisher")
    {
        using var response = await client.PostAsJsonAsync("/api/publishers", new PublisherRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var publisher = (await response.Content.ReadFromJsonAsync<PublisherDto>())!;
        Assert.Equal($"/api/publishers/{publisher.Id}", response.Headers.Location!.AbsolutePath);
        return publisher;
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("GET", true)]
    [InlineData("POST", false)]
    [InlineData("PUT", true)]
    [InlineData("DELETE", true)]
    public async Task Every_endpoint_rejects_anonymous_access(string method, bool item)
    {
        using var client = Client(null);
        using var request = new HttpRequestMessage(new HttpMethod(method), item ? $"/api/publishers/{Guid.NewGuid()}" : "/api/publishers");
        if (method is "POST" or "PUT") request.Content = JsonContent.Create(new PublisherRequest("Valid name"));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Staff_member_is_forbidden_on_every_write_and_data_is_unchanged(string method)
    {
        using var writer = Client(StaffRoles.Librarian);
        var publisher = await Create(writer);
        using var client = Client(StaffRoles.StaffMember);
        using var request = new HttpRequestMessage(new HttpMethod(method), method == "POST" ? "/api/publishers" : $"/api/publishers/{publisher.Id}");
        if (method is "POST" or "PUT") request.Content = JsonContent.Create(new PublisherRequest("Changed"));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(publisher, await client.GetFromJsonAsync<PublisherDto>($"/api/publishers/{publisher.Id}"));
        Assert.DoesNotContain((await client.GetFromJsonAsync<PublisherDto[]>("/api/publishers"))!, x => x.Name == "Changed");
    }

    [Theory]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    public async Task Both_writer_roles_can_create_update_and_delete(string role)
    {
        using var client = Client(role);
        var publisher = await Create(client, "  Original Publisher  ");
        Assert.Equal("Original Publisher", publisher.Name);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/publishers/{publisher.Id}", new PublisherRequest("Updated Publisher"))).StatusCode);
        Assert.Equal("Updated Publisher", (await client.GetFromJsonAsync<PublisherDto>($"/api/publishers/{publisher.Id}"))!.Name);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/publishers/{publisher.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/publishers/{publisher.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(StaffRoles.StaffMember)]
    [InlineData(StaffRoles.Librarian)]
    [InlineData(StaffRoles.LibraryAdministrator)]
    [InlineData("")]
    public async Task Any_authenticated_user_can_read_collection_and_item(string role)
    {
        using var writer = Client(StaffRoles.Librarian);
        var publisher = await Create(writer);
        using var client = Client(role);
        Assert.Equal(publisher, await client.GetFromJsonAsync<PublisherDto>($"/api/publishers/{publisher.Id}"));
        Assert.Contains((await client.GetFromJsonAsync<PublisherDto[]>("/api/publishers"))!, x => x == publisher);
    }

    public static IEnumerable<object[]> InvalidNames()
    {
        yield return ["{}"];
        yield return ["{\"name\":null}"];
        yield return [JsonSerializer.Serialize(new PublisherRequest(""))];
        yield return [JsonSerializer.Serialize(new PublisherRequest(" \t\r\n"))];
        yield return [JsonSerializer.Serialize(new PublisherRequest(new string('a', 201)))];
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task Invalid_names_return_400_on_create_and_update(string body)
    {
        using var client = Client(StaffRoles.Librarian);
        var publisher = await Create(client);
        using var create = await client.PostAsync("/api/publishers", new StringContent(body, Encoding.UTF8, "application/json"));
        await AssertProblem(create, HttpStatusCode.BadRequest, "validation_failed");
        using var update = await client.PutAsync($"/api/publishers/{publisher.Id}", new StringContent(body, Encoding.UTF8, "application/json"));
        await AssertProblem(update, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(publisher, await client.GetFromJsonAsync<PublisherDto>($"/api/publishers/{publisher.Id}"));
    }

    [Fact]
    public async Task Names_of_200_characters_are_accepted_and_names_need_not_be_unique()
    {
        using var client = Client(StaffRoles.Librarian);
        var name = new string('a', 200);
        var first = await Create(client, name);
        var second = await Create(client, name);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(name, first.Name);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/publishers/{second.Id}", new PublisherRequest(name))).StatusCode);
        Assert.Equal(name, (await client.GetFromJsonAsync<PublisherDto>($"/api/publishers/{second.Id}"))!.Name);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Missing_items_return_404_problem_details(string method)
    {
        using var client = Client(StaffRoles.Librarian);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/publishers/{Guid.NewGuid()}");
        if (method == "PUT") request.Content = JsonContent.Create(new PublisherRequest("Valid name"));
        using var response = await client.SendAsync(request);
        await AssertProblem(response, HttpStatusCode.NotFound, "catalog.publisher_not_found");
    }

    [Fact]
    public async Task Swagger_documents_all_routes_statuses_and_xml_summaries()
    {
        using var client = Client(null);
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = swagger.GetProperty("paths");
        var collection = paths.GetProperty("/api/publishers");
        var item = paths.GetProperty("/api/publishers/{id}");
        Check(collection.GetProperty("get"), "Lists publishers.", ["200", "401"]);
        Check(collection.GetProperty("post"), "Creates a publisher.", ["201", "400", "401", "403"]);
        Check(item.GetProperty("get"), "Gets a publisher.", ["200", "401", "404"]);
        Check(item.GetProperty("put"), "Renames a publisher.", ["204", "400", "401", "403", "404"]);
        Check(item.GetProperty("delete"), "Deletes a publisher only when no book references it, deleted books included.", ["204", "401", "403", "404", "409"]);
        Assert.True(swagger.GetProperty("security")[0].TryGetProperty("Bearer", out _));

        static void Check(JsonElement operation, string summary, string[] statuses)
        {
            Assert.Equal(summary, operation.GetProperty("summary").GetString());
            foreach (var status in statuses) Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        }
    }

    protected static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
