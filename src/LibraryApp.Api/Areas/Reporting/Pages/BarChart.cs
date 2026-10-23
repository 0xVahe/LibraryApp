using System.Globalization;

namespace LibraryApp.Api.Areas.Reporting.Pages;

/// <summary>One bar: the name, the value it is drawn from and the text shown next to it.</summary>
public sealed record Bar(string Name, decimal Value, string Text, bool Warning = false);

/// <summary>A horizontal bar chart of the Reports page (REP-09); the longest bar is the largest value.</summary>
public sealed record BarChartModel(string Id, string Title, string Note, IReadOnlyList<Bar> Bars)
{
    /// <summary>The width of a bar in the 0–100 box of the SVG.</summary>
    public string Width(Bar bar)
    {
        decimal max = Bars.Max(b => b.Value);
        decimal width = max <= 0 ? 0 : Math.Round(bar.Value * 100m / max, 2);
        return width.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
