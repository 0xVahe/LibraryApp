namespace LibraryApp.Application.Common;

/// <summary>
/// One page of a list (SD-14). Every list endpoint returns this shape; <see cref="TotalCount"/> counts all matching
/// items, not only this page.
/// </summary>
/// <param name="Items">The items of this page, in the order of the list.</param>
/// <param name="TotalCount">All matching items, on every page.</param>
/// <param name="Page">The page number, from 1.</param>
/// <param name="PageSize">Items per page (default 20, at most 100).</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    /// <summary>The number of pages; 0 when nothing matches.</summary>
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Empty(PageRequest request) => new([], 0, request.Page, request.PageSize);
}

/// <summary>
/// Page number (from 1) and size (default 20, at most 100 — SD-14; CAT-05 asks for 20). Out-of-range values are
/// corrected, not rejected, so a client can always ask for "page=1" without knowing the limits. A page far past the
/// end is an empty page, never an error.
/// </summary>
public sealed record PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public PageRequest(int page = 1, int pageSize = DefaultPageSize)
    {
        Page = Math.Max(1, page);
        PageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
    }

    public int Page { get; }

    public int PageSize { get; }

    /// <summary>
    /// Rows to skip for this page. Computed in 64 bits and capped at <see cref="int.MaxValue"/>: page 2147483647 would
    /// otherwise overflow to a negative OFFSET, which SQL Server rejects (500 instead of an empty page).
    /// </summary>
    public int Skip => (int)Math.Min((long)(Page - 1) * PageSize, int.MaxValue);
}
