using System.Net;
using System.Text.RegularExpressions;
using LibraryApp.Api.IntegrationTests.Ui;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Infrastructure.Persistence.Seed.Borrowing;
using LibraryApp.Infrastructure.Persistence.Seed.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Api.IntegrationTests.Borrowing;

/// <summary>
/// The host for the Borrowing pages and blocks (G3-19): the Catalog and Staff seed without the Borrowing seed (the tests
/// own every loan, reservation and penalty), plus a test controller that renders the blocks the way Group 2's My library
/// page will.
/// </summary>
public sealed class BorrowingUiHost : ApiHostFactory
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.Remove(services.Single(d => d.ServiceType == typeof(IModuleSeeder) && d.ImplementationType == typeof(BorrowingSeeder)));
        services.AddControllers().AddApplicationPart(typeof(BorrowingBlocksTestController).Assembly);
    }
}

[Route("ui-test/borrowing/blocks")]
[Authorize]
public sealed class BorrowingBlocksTestController : Controller
{
    /// <summary>What My library and Staff member do: <c>&lt;vc:my-loans staff-member-id="…" /&gt;</c>.</summary>
    [HttpGet("{component}/{staffMemberId:guid}")]
    public IActionResult Block(string component, Guid staffMemberId) => ViewComponent(component, new { staffMemberId });
}

/// <summary>A browser in the page tests: the UI cookie, the antiforgery token of a page, form posts, the next page.</summary>
public sealed class UiBrowser(ApiHostFactory factory, string cookies)
{
    public static async Task<UiBrowser> SignInAsync(ApiHostFactory factory, Guid staffMemberId) =>
        new(factory, await UiTestClient.CookieAsync(factory,
            StaffSeedData.People.Single(p => p.StaffMemberId == staffMemberId).Email, StaffSeedData.Password));

    public static UiBrowser Anonymous(ApiHostFactory factory) => new(factory, "");

    public string Cookies { get; private set; } = cookies;

    public async Task<(HttpStatusCode Status, string Html, HttpResponseMessage Response)> GetAsync(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (Cookies.Length > 0)
            request.Headers.Add("Cookie", Cookies);
        var response = await UiTestClient.Anonymous(factory).SendAsync(request);
        Keep(response);
        // Decoded like the other page tests: Razor writes "·" and "—" as character references.
        return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), response);
    }

    /// <summary>Posts a form like a browser: the antiforgery token of <paramref name="formPage"/> is added.</summary>
    public async Task<HttpResponseMessage> PostAsync(string formPage, string action, params (string Name, string Value)[] fields)
    {
        var (_, html, _) = await GetAsync(formPage);
        string token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        if (token.Length == 0)
            throw new InvalidOperationException($"{formPage} has no form with an antiforgery token.");
        return await PostRawAsync(action, [.. fields, ("__RequestVerificationToken", token)]);
    }

    public async Task<HttpResponseMessage> PostRawAsync(string action, params (string Name, string Value)[] fields)
    {
        var post = new HttpRequestMessage(HttpMethod.Post, action)
        {
            Content = new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value))),
        };
        if (Cookies.Length > 0)
            post.Headers.Add("Cookie", Cookies);
        var response = await UiTestClient.Anonymous(factory).SendAsync(post);
        Keep(response);
        return response;
    }

    /// <summary>The HTML of a page answered to a post (a form with its errors), decoded.</summary>
    public static async Task<string> HtmlAsync(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

    /// <summary>The page after a redirect (Post/Redirect/Get), with the alert kept in TempData.</summary>
    public async Task<string> FollowAsync(HttpResponseMessage redirect)
    {
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        return (await GetAsync(redirect.Headers.Location!.OriginalString)).Html;
    }

    /// <summary>
    /// The alert of a redirect (Post/Redirect/Get) when the target is a block without the shared layout: any page with
    /// the layout shows the alert kept in TempData.
    /// </summary>
    public async Task<string> AlertAfterAsync(HttpResponseMessage redirect)
    {
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        return (await GetAsync("/forbidden")).Html;
    }

    /// <summary>Cookies set by a response replace the ones of the same name (antiforgery, TempData).</summary>
    private void Keep(HttpResponseMessage response)
    {
        string set = UiTestClient.SetCookies(response);
        if (set.Length == 0)
            return;
        var jar = Cookies.Split("; ", StringSplitOptions.RemoveEmptyEntries).Select(c => c.Split('=', 2))
            .ToDictionary(c => c[0], c => c.Length > 1 ? c[1] : "");
        foreach (var cookie in set.Split("; ").Select(c => c.Split('=', 2)))
        {
            if (cookie.Length > 1 && cookie[1].Length > 0)
                jar[cookie[0]] = cookie[1];
            else
                jar.Remove(cookie[0]);
        }
        Cookies = string.Join("; ", jar.Select(c => $"{c.Key}={c.Value}"));
    }
}
