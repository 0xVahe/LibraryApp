using LibraryApp.Application.Common;

namespace LibraryApp.Application.Catalog.Dto;

public sealed record BookSearchRequest(
    string? Search = null,
    string? Category = null,
    string? Language = null,
    bool? Available = null,
    PageRequest? Paging = null)
{
    public PageRequest Page => Paging ?? new PageRequest();
}
