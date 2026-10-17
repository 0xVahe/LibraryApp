using System.Text.Json;
using LibraryApp.Application.Common;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;

namespace LibraryApp.Api.Ui.Shared;

/// <summary>Colours of <c>_Alert</c>: green success, amber warning, red error (only red gets <c>role="alert"</c>).</summary>
public enum AlertKind
{
    Green = 1,
    Amber = 2,
    Red = 3,
}

public sealed record AlertModel(AlertKind Kind, string Title, string? Text = null)
{
    public string CssClass => "alert alert-" + Kind.ToString().ToLowerInvariant();
}

/// <summary>The alert shown once on the next page (Post/Redirect/Get). <c>_Layout</c> renders it above the content.</summary>
public static class UiAlerts
{
    private const string Key = "ui.alert";

    public static void SetAlert(this ITempDataDictionary tempData, AlertKind kind, string title, string? text = null) =>
        tempData[Key] = JsonSerializer.Serialize(new AlertModel(kind, title, text));

    public static AlertModel? TakeAlert(this ITempDataDictionary tempData) =>
        tempData[Key] is string json ? JsonSerializer.Deserialize<AlertModel>(json) : null;
}

public sealed record PageAction(string Text, string Href, bool Primary = false);

/// <summary>Model of <c>_PageHead</c>: h1, one sentence, optional breadcrumbs and buttons on the right.</summary>
public sealed record PageHeadModel(
    string Title,
    string? Subtitle = null,
    IReadOnlyList<PageAction>? Actions = null,
    IReadOnlyList<PageAction>? Crumbs = null);

public sealed record PagerLink(int Page, string Href, bool Current);

/// <summary>Model of <c>_Pager</c>, built from a <see cref="PagedResult{T}"/>; every link keeps the other query values.</summary>
public sealed record PagerModel(IReadOnlyList<PagerLink> Links)
{
    public const string PageParameter = "page";

    public static PagerModel From<T>(PagedResult<T> result, HttpRequest request)
    {
        if (result.TotalPages <= 1)
            return new([]);

        var query = QueryHelpers.ParseQuery(request.QueryString.Value)
            .Where(pair => !string.Equals(pair.Key, PageParameter, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)))
            .ToList();

        var links = Enumerable.Range(1, result.TotalPages)
            .Select(page => new PagerLink(page,
                QueryHelpers.AddQueryString(request.Path, [.. query, new(PageParameter, page.ToString())]),
                page == result.Page))
            .ToList();
        return new(links);
    }
}
