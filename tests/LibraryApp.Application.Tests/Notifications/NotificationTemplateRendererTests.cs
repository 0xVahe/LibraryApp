using LibraryApp.Application.Notifications.Services;

namespace LibraryApp.Application.Tests.Notifications;

public class NotificationTemplateRendererTests
{
    private readonly NotificationTemplateRenderer _renderer = new();

    private static readonly Dictionary<string, string> Values = new()
    {
        [NotificationPlaceholders.EmployeeName] = "Anna Petrosyan",
        [NotificationPlaceholders.BookTitle] = "Clean Code",
        [NotificationPlaceholders.DueDate] = "05.11.2026",
    };

    [Fact]
    public void Replaces_placeholders()
    {
        var result = _renderer.Render(
            "Your loan of \"{{BookTitle}}\"",
            "<p>Dear {{EmployeeName}},</p><p>Return \"{{BookTitle}}\" by {{DueDate}}.</p>",
            Values);

        Assert.Equal("Your loan of \"Clean Code\"", result.Subject);
        Assert.Contains("<p>Dear Anna Petrosyan,</p>", result.HtmlBody);
        Assert.Contains("by 05.11.2026.", result.HtmlBody);
        Assert.DoesNotContain("{{", result.HtmlBody);
    }

    [Fact]
    public void Body_values_are_html_encoded()
    {
        var values = new Dictionary<string, string>(Values) { [NotificationPlaceholders.BookTitle] = "C# <in depth> & more" };

        var result = _renderer.Render("{{BookTitle}}", "<p>{{BookTitle}}</p>", values);

        Assert.Contains("<p>C# &lt;in depth&gt; &amp; more</p>", result.HtmlBody);
        Assert.Equal("C# <in depth> & more", result.Subject);
    }

    [Fact]
    public void Subject_is_one_line()
    {
        var values = new Dictionary<string, string>(Values) { [NotificationPlaceholders.BookTitle] = "Clean\r\nCode" };

        var result = _renderer.Render("Loan: {{BookTitle}}", "<p>Body</p>", values);

        Assert.Equal("Loan: Clean Code", result.Subject);
    }

    [Fact]
    public void Long_subject_is_cut_to_300()
    {
        var values = new Dictionary<string, string>(Values) { [NotificationPlaceholders.BookTitle] = new string('x', 400) };

        var result = _renderer.Render("Loan: {{BookTitle}}", "<p>Body</p>", values);

        Assert.Equal(300, result.Subject.Length);
    }

    [Fact]
    public void Missing_value_fails_with_names_only()
    {
        var values = new Dictionary<string, string> { [NotificationPlaceholders.EmployeeName] = "Anna Petrosyan" };

        var ex = Assert.Throws<ArgumentException>(() =>
            _renderer.Render("{{BookTitle}}", "<p>{{EmployeeName}}, {{DueDate}}</p>", values));

        Assert.Contains("BookTitle", ex.Message);
        Assert.Contains("DueDate", ex.Message);
        Assert.DoesNotContain("Anna", ex.Message);
    }

    [Fact]
    public void Cut_keeps_whole_emoji()
    {
        // "Loan: " is 6 characters, so the emoji starts at index 299 and the cut at 300 would split it.
        var values = new Dictionary<string, string>(Values)
        {
            [NotificationPlaceholders.BookTitle] = new string('x', 293) + "\U0001F600" + "tail",
        };

        var result = _renderer.Render("Loan: {{BookTitle}}", "<p>Body</p>", values);

        Assert.Equal(299, result.Subject.Length);
        Assert.False(char.IsHighSurrogate(result.Subject[^1]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_value_counts_as_missing(string blank)
    {
        var values = new Dictionary<string, string>(Values) { [NotificationPlaceholders.EmployeeName] = blank };

        var ex = Assert.Throws<ArgumentException>(() => _renderer.Render("Welcome", "<p>Dear {{EmployeeName}},</p>", values));

        Assert.Contains("EmployeeName", ex.Message);
    }

    [Fact]
    public void Placeholder_with_spaces_renders()
    {
        var result = _renderer.Render("{{ BookTitle }}", "<p>Dear {{ EmployeeName }},</p>", Values);

        Assert.Equal("Clean Code", result.Subject);
        Assert.Contains("Dear Anna Petrosyan,", result.HtmlBody);
    }

    [Theory]
    [InlineData("<p>{{Book.Title}}</p>")]
    [InlineData("<p>{{Book-Title}}</p>")]
    [InlineData("<p>Dear {{EmployeeName</p>")]
    [InlineData("<p>Dear EmployeeName}}</p>")]
    public void Malformed_placeholder_fails(string body)
    {
        Assert.Throws<ArgumentException>(() => _renderer.Render("Welcome", body, Values));
        Assert.False(NotificationPlaceholders.IsWellFormed(body));
    }

    [Fact]
    public void Extra_values_are_ignored()
    {
        var result = _renderer.Render("Welcome", "<p>Dear {{EmployeeName}}</p>", Values);

        Assert.Equal("Welcome", result.Subject);
        Assert.Contains("Dear Anna Petrosyan", result.HtmlBody);
    }

    [Fact]
    public void Body_is_wrapped_in_layout()
    {
        var result = _renderer.Render("Welcome", "<p>Hello</p>", Values);

        Assert.StartsWith("<!DOCTYPE html>", result.HtmlBody);
        Assert.Contains("Ameriabank Corporate Library", result.HtmlBody);
        Assert.Contains("<p>Hello</p>", result.HtmlBody);
        Assert.EndsWith("</html>", result.HtmlBody);
    }

    [Fact]
    public void Dates_use_day_month_year()
    {
        Assert.Equal("05.11.2026", NotificationPlaceholders.FormatDate(new DateOnly(2026, 11, 5)));
    }

    [Fact]
    public void Find_returns_each_name_once_in_order()
    {
        var names = NotificationPlaceholders.Find("{{BookTitle}} {{EmployeeName}} {{BookTitle}} {{ not one }}");

        Assert.Equal(new[] { "BookTitle", "EmployeeName" }, names);
    }
}
