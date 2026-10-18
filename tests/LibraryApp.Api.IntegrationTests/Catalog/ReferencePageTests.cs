using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Catalog;

public class ReferencePageTests(ApiHostFactory factory) : IClassFixture<ApiHostFactory>
{
    public static TheoryData<string, string, string> Pages => new()
    {
        { "authors", "Author", "FullName" },
        { "categories", "Category", "Name" },
        { "publishers", "Publisher", "Name" },
    };

    public static IEnumerable<object[]> StaffPages =>
        from page in Pages
        from administrator in new[] { false, true }
        select new object[] { page[0], page[1], page[2], administrator };

    [Theory]
    [MemberData(nameof(StaffPages))]
    public async Task Library_staff_can_read_every_item_and_its_forms(
        string page, string singular, string field, bool administrator)
    {
        using var client = await SignedIn(administrator ? StaffSeedIds.StaffMembers.LilitGrigoryan : StaffSeedIds.StaffMembers.AramSargsyan);
        using var response = await client.GetAsync(Path(page));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var items = await ListAsync(page);
        Assert.NotEmpty(items);
        Assert.Contains("<a href=\"/catalog\">Catalog</a>", html);
        Assert.Contains($"Create {singular.ToLowerInvariant()}", html);
        Assert.Contains($"name=\"{field}\"", Form(html, "Create"));
        foreach (var item in items)
        {
            Assert.Contains(item.Name, WebUtility.HtmlDecode(html));
            Assert.Contains(item.Name, WebUtility.HtmlDecode(Form(html, "Edit", item.Id)));
            var deleteForm = Form(html, "Delete", item.Id);
            Assert.Contains("type=\"checkbox\" name=\"confirmDelete\" value=\"true\"", deleteForm);
            Assert.Contains($"I want to delete this {singular.ToLowerInvariant()}", deleteForm);
        }

        foreach (Match form in Regex.Matches(html, "<form\\b.*?</form>", RegexOptions.Singleline))
        {
            Assert.Single(Regex.Matches(form.Value, "<form\\b").Cast<Match>());
            Assert.Contains("method=\"post\"", form.Value);
            Assert.Contains("name=\"__RequestVerificationToken\"", form.Value);
        }
    }

    [Theory]
    [MemberData(nameof(StaffPages))]
    public async Task Both_library_staff_roles_can_create_rename_and_delete_with_success_alerts(
        string page, string singular, string field, bool administrator)
    {
        using var client = await SignedIn(administrator ? StaffSeedIds.StaffMembers.LilitGrigoryan : StaffSeedIds.StaffMembers.AramSargsyan);
        var before = await ListAsync(page);
        var name = $"New {singular} {Guid.NewGuid():N}";
        using var created = await PostAsync(client, page, "Create", new() { [field] = name });
        await AssertSuccessAsync(client, created, page, $"{singular} created.");
        var item = Assert.Single(await ListAsync(page), item => before.All(old => old.Id != item.Id));
        Assert.Equal(name, item.Name);

        var renamed = name + " renamed";
        using var edited = await PostAsync(client, page, "Edit", new() { ["id"] = item.Id.ToString(), [field] = renamed });
        var editHtml = await AssertSuccessAsync(client, edited, page, $"{singular} updated.");
        Assert.Contains(renamed, editHtml);
        Assert.Equal(renamed, (await ListAsync(page)).Single(row => row.Id == item.Id).Name);

        // Delete binds only the ID and confirmation; no unrelated name is needed.
        using var deleted = await PostAsync(client, page, "Delete", new() { ["id"] = item.Id.ToString(), ["confirmDelete"] = "true" });
        var deleteHtml = await AssertSuccessAsync(client, deleted, page, $"{singular} deleted.");
        Assert.DoesNotContain(renamed, deleteHtml);
        Assert.DoesNotContain(await ListAsync(page), row => row.Id == item.Id);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Deleting_without_confirmation_keeps_an_unused_item(string page, string singular, string field)
    {
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AramSargsyan);
        var beforeCreate = await ListAsync(page);
        using var created = await PostAsync(client, page, "Create", new() { [field] = $"Unconfirmed {singular}" });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var beforeDelete = await ListAsync(page);
        var item = Assert.Single(beforeDelete, item => beforeCreate.All(old => old.Id != item.Id));

        using var response = await PostAsync(client, page, "Delete", new() { ["id"] = item.Id.ToString() });

        var html = await AssertErrorAsync(response, $"Tick \"I want to delete this {singular.ToLowerInvariant()}\" to confirm.");
        Assert.Equal(beforeDelete, await ListAsync(page));
        Assert.Contains(item.Name, Form(html, "Edit", item.Id));
        Assert.DoesNotContain($"{singular} deleted.", html);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Deleting_a_reference_used_by_a_book_shows_the_service_error_and_preserves_it(
        string page, string singular, string field)
    {
        var (id, error) = page switch
        {
            "authors" => (CatalogSeedData.Authors[0].Id, CatalogErrors.AuthorInUse.Message),
            "categories" => (CatalogSeedIds.Category(1), CatalogErrors.CategoryInUse.Message),
            "publishers" => (CatalogSeedData.Publishers[0].Id, CatalogErrors.PublisherInUse.Message),
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };
        var before = (await ListAsync(page)).Single(item => item.Id == id);
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AramSargsyan);
        using var response = await PostAsync(client, page, "Delete", new() { ["id"] = id.ToString(), ["confirmDelete"] = "true" });

        var html = await AssertErrorAsync(response, error);
        Assert.Equal(before, (await ListAsync(page)).Single(item => item.Id == id));
        Assert.Contains($"name=\"{field}\"", Form(html, "Create"));
        Assert.Contains(before.Name, WebUtility.HtmlDecode(Form(html, "Edit", id)));
        Assert.DoesNotContain($"{singular} deleted.", html);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Invalid_names_reload_the_list_and_preserve_only_the_submitted_form(
        string page, string singular, string field)
    {
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AramSargsyan);
        var before = await ListAsync(page);
        using var blank = await PostAsync(client, page, "Create", new() { [field] = "" });
        var blankHtml = await AssertErrorAsync(blank, "must not be empty");
        var blankInput = Regex.Match(Form(blankHtml, "Create"), "<input id=\"create-name\"[^>]*>").Value;
        Assert.NotEmpty(blankInput);
        Assert.Empty(Regex.Match(blankInput, "value=\"([^\"]*)\"").Groups[1].Value);
        Assert.Equal(before, await ListAsync(page));

        var tooLong = new string('X', page == "categories" ? 101 : 201);
        using var invalidCreate = await PostAsync(client, page, "Create", new() { [field] = tooLong });
        var createHtml = await AssertErrorAsync(invalidCreate, "characters or fewer");
        Assert.Contains(tooLong, Form(createHtml, "Create"));
        Assert.Equal(before, await ListAsync(page));

        var item = before[0];
        using var invalidEdit = await PostAsync(client, page, "Edit", new() { ["id"] = item.Id.ToString(), [field] = tooLong });
        var editHtml = await AssertErrorAsync(invalidEdit, "characters or fewer");
        Assert.Contains(tooLong, Form(editHtml, "Edit", item.Id));
        Assert.DoesNotContain(tooLong, Form(editHtml, "Create"));
        Assert.Contains(before[1].Name, WebUtility.HtmlDecode(Form(editHtml, "Edit", before[1].Id)));
        Assert.Equal(before, await ListAsync(page));
        Assert.DoesNotContain($"{singular} updated.", editHtml);
    }

    [Fact]
    public async Task Category_parent_options_include_no_parent_and_every_other_top_level_category()
    {
        var parent = await CreateCategoryAsync("Parent options root");
        var child = await CreateCategoryAsync("Parent options child", parent.Id);
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AramSargsyan);
        using var response = await client.GetAsync(Path("categories"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var categories = await CategoriesAsync();
        var roots = categories.Where(category => category.ParentCategoryId is null).Select(category => category.Id.ToString()).ToArray();

        AssertParentOptions(Form(html, "Create"), roots);
        foreach (var category in categories)
            AssertParentOptions(Form(html, "Edit", category.Id), categories.Any(item => item.ParentCategoryId == category.Id)
                ? [] : roots.Where(id => id != category.Id.ToString()));
        Assert.Contains($"<td data-label=\"Name\">{parent.Name} / {child.Name}</td>", WebUtility.HtmlDecode(html));
        Assert.Contains($"value=\"{parent.Id}\" selected=\"selected\"", Form(html, "Edit", child.Id));
    }

    [Fact]
    public async Task A_category_can_be_created_under_a_parent_and_edited_back_to_no_parent()
    {
        var parent = await CreateCategoryAsync("Optional parent");
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AramSargsyan);
        var before = await CategoriesAsync();
        using var created = await PostAsync(client, "categories", "Create", new()
        {
            ["Name"] = "New child", ["ParentCategoryId"] = parent.Id.ToString(),
        });
        await AssertSuccessAsync(client, created, "categories", "Category created.");
        var child = Assert.Single(await CategoriesAsync(), item => before.All(old => old.Id != item.Id));
        Assert.Equal(parent.Id, child.ParentCategoryId);

        using var edited = await PostAsync(client, "categories", "Edit", new()
        {
            ["id"] = child.Id.ToString(), ["Name"] = "Now top-level", ["ParentCategoryId"] = "",
        });
        await AssertSuccessAsync(client, edited, "categories", "Category updated.");
        var updated = (await CategoriesAsync()).Single(category => category.Id == child.Id);
        Assert.Null(updated.ParentCategoryId);
        Assert.Equal("Now top-level", updated.Name);
    }

    [Theory]
    [InlineData("child-parent")]
    [InlineData("has-children")]
    [InlineData("self")]
    public async Task Invalid_hierarchy_edits_show_the_service_error_and_leave_the_hierarchy_unchanged(string change)
    {
        var root = await CreateCategoryAsync("Hierarchy root");
        var child = await CreateCategoryAsync("Hierarchy child", root.Id);
        var other = await CreateCategoryAsync("Hierarchy other root");
        var (item, parentId, error) = change switch
        {
            "child-parent" => (other, child.Id, CatalogErrors.CategoryParentIsNotTopLevel.Message),
            "has-children" => (root, other.Id, CatalogErrors.CategoryHasSubcategories.Message),
            "self" => (other, other.Id, CatalogErrors.CategoryParentIsItself.Message),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        var before = await CategoriesAsync();
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AramSargsyan);
        using var response = await PostAsync(client, "categories", "Edit", new()
        {
            ["id"] = item.Id.ToString(), ["Name"] = "Submitted category name", ["ParentCategoryId"] = parentId.ToString(),
        });

        var html = await AssertErrorAsync(response, error);
        Assert.Equal(before, await CategoriesAsync());
        Assert.Contains("Submitted category name", Form(html, "Edit", item.Id));
        AssertParentOptions(Form(html, "Edit", item.Id),
            change == "has-children" ? [] : before.Where(category => category.ParentCategoryId is null && category.Id != item.Id)
                .Select(category => category.Id.ToString()));
    }

    [Fact]
    public async Task Creating_a_third_level_displays_the_two_level_error_and_keeps_the_create_name()
    {
        var root = await CreateCategoryAsync("Create hierarchy root");
        var child = await CreateCategoryAsync("Create hierarchy child", root.Id);
        var before = await CategoriesAsync();
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AramSargsyan);
        using var response = await PostAsync(client, "categories", "Create", new()
        {
            ["Name"] = "Third level", ["ParentCategoryId"] = child.Id.ToString(),
        });

        var html = await AssertErrorAsync(response, CatalogErrors.CategoryParentIsNotTopLevel.Message);
        Assert.Contains("Third level", Form(html, "Create"));
        Assert.Equal(before, await CategoriesAsync());
        AssertParentOptions(Form(html, "Create"), before.Where(category => category.ParentCategoryId is null).Select(category => category.Id.ToString()));
    }

    [Fact]
    public async Task Deleting_a_category_with_children_displays_the_in_use_error()
    {
        var parent = await CreateCategoryAsync("Delete parent");
        await CreateCategoryAsync("Delete child", parent.Id);
        var before = await CategoriesAsync();
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AramSargsyan);
        using var response = await PostAsync(client, "categories", "Delete", new() { ["id"] = parent.Id.ToString(), ["confirmDelete"] = "true" });

        await AssertErrorAsync(response, CatalogErrors.CategoryInUse.Message);
        Assert.Equal(before, await CategoriesAsync());
    }

    [Theory]
    [InlineData("authors")]
    [InlineData("categories")]
    [InlineData("publishers")]
    public async Task Staff_member_gets_Forbidden_from_each_actual_page_route(string page)
    {
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AnnaPetrosyan);
        using var response = await client.GetAsync(Path(page));
        await AssertForbiddenAsync(client, response);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Staff_member_cannot_create_edit_or_delete_with_valid_antiforgery(string page, string singular, string field)
    {
        using var librarian = await SignedIn(StaffSeedIds.StaffMembers.AramSargsyan);
        var beforeCreate = await ListAsync(page);
        using var created = await PostAsync(librarian, page, "Create", new() { [field] = $"Protected {singular}" });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var item = Assert.Single(await ListAsync(page), item => beforeCreate.All(old => old.Id != item.Id));
        var before = await ListAsync(page);
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AnnaPetrosyan);
        foreach (var handler in new[] { "Create", "Edit", "Delete" })
        {
            // /forbidden uses the real layout, giving this authenticated user their own antiforgery token and cookie.
            using var response = await PostAsync(client, page, handler, new()
            {
                ["id"] = item.Id.ToString(), [field] = "Unauthorized change",
                ["confirmDelete"] = "true",
            }, tokenPath: "/forbidden");
            await AssertForbiddenAsync(client, response);
            Assert.Equal(before, await ListAsync(page));
        }
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task A_missing_antiforgery_token_rejects_mutations(string page, string singular, string field)
    {
        using var client = await SignedIn(StaffSeedIds.StaffMembers.AramSargsyan);
        var before = await ListAsync(page);
        using var response = await client.PostAsync(Path(page) + "?handler=Create",
            new FormUrlEncodedContent(new Dictionary<string, string> { [field] = $"Unprotected {singular}" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await ListAsync(page));
    }

    private Task<HttpClient> SignedIn(Guid staffId) => UiTestClient.SignedInAsync(factory,
        StaffSeedData.People.Single(person => person.StaffMemberId == staffId).Email, StaffSeedData.Password);

    private static string Path(string page) => "/catalog/" + page;

    private sealed record Item(Guid Id, string Name);

    private async Task<Item[]> ListAsync(string page)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        return page switch
        {
            "authors" => (await services.GetRequiredService<IAuthorService>().ListAsync()).Select(item => new Item(item.Id, item.FullName)).ToArray(),
            "categories" => (await services.GetRequiredService<ICategoryService>().ListAsync()).Select(item => new Item(item.Id, item.Name)).ToArray(),
            "publishers" => (await services.GetRequiredService<IPublisherService>().ListAsync()).Select(item => new Item(item.Id, item.Name)).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };
    }

    private async Task<IReadOnlyList<CategoryDto>> CategoriesAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICategoryService>().ListAsync();
    }

    private async Task<CategoryDto> CreateCategoryAsync(string name, Guid? parentId = null)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICategoryService>().CreateAsync(new CategoryRequest(name, parentId));
    }

    private static string Form(string html, string handler, Guid? id = null) =>
        Assert.Single(Regex.Matches(html, "<form\\b.*?</form>", RegexOptions.Singleline).Cast<Match>()
            .Select(match => WebUtility.HtmlDecode(match.Value)),
            form => form.Contains($"handler={handler}\"", StringComparison.Ordinal)
                && (!id.HasValue || form.Contains($"name=\"id\" value=\"{id}\"", StringComparison.Ordinal)));

    private static void AssertParentOptions(string form, IEnumerable<string> expectedIds)
    {
        var select = Regex.Match(form, "<select[^>]*name=\"ParentCategoryId\"[^>]*>(.*?)</select>", RegexOptions.Singleline).Value;
        Assert.NotEmpty(select);
        Assert.Contains("<option value=\"\">No parent</option>", select);
        var values = Regex.Matches(select, "<option value=\"([^\"]*)\"").Cast<Match>().Select(match => match.Groups[1].Value);
        Assert.Equal(expectedIds.Append("").OrderBy(id => id), values.OrderBy(id => id));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string page, string handler,
        Dictionary<string, string> fields, string? tokenPath = null)
    {
        using var form = await client.GetAsync(tokenPath ?? Path(page));
        var token = Regex.Match(await form.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token);
        using var request = new HttpRequestMessage(HttpMethod.Post, Path(page) + "?handler=" + handler)
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Add("Cookie", Cookies(client, form));
        return await client.SendAsync(request);
    }

    private static string Cookies(HttpClient client, HttpResponseMessage response) =>
        string.Join("; ", client.DefaultRequestHeaders.GetValues("Cookie")) + "; " + UiTestClient.SetCookies(response);

    private static async Task<string> AssertSuccessAsync(HttpClient client, HttpResponseMessage response, string page, string message)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Path(page), response.Headers.Location!.OriginalString);
        using var request = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location);
        request.Headers.Add("Cookie", Cookies(client, response));
        using var redirected = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, redirected.StatusCode);
        var html = await redirected.Content.ReadAsStringAsync();
        Assert.Contains("alert alert-green", html);
        Assert.Contains(message, html);
        return html;
    }

    private static async Task<string> AssertErrorAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("alert alert-red", html);
        Assert.Contains("role=\"alert\"", html);
        Assert.Contains(message, html);
        return html;
    }

    private static async Task AssertForbiddenAsync(HttpClient client, HttpResponseMessage response)
    {
        // The shared cookie scheme redirects a denied page request to the existing HTTP 403 page.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forbidden", response.Headers.Location!.AbsolutePath);
        using var forbidden = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
