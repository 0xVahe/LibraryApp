namespace LibraryApp.Application.Common;

/// <summary>
/// One page of a list (SD-14). Every list endpoint returns this shape; <see cref="TotalCount"/> counts all matching
/// items, not only this page.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static PagedResult<T> Empty(PageRequest request) => new([], 0, request.Page, request.PageSize);
}

/// <summary>
/// Page number (from 1) and size (default 20, at most 100 — SD-14; CAT-05 asks for 20). Out-of-range values are
/// corrected, not rejected, so a client can always ask for "page=1" without knowing the limits.
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

    /// <summary>Rows to skip for this page.</summary>
    public int Skip => (Page - 1) * PageSize;
}
