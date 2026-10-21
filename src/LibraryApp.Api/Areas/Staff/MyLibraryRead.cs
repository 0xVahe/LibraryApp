using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Services;

namespace LibraryApp.Api.Areas.Staff;

/// <summary>The page and profile component share the caller's summary within one request only.</summary>
internal static class MyLibraryRead
{
    private static readonly object Key = new();

    public static Task<MyLibraryDto> GetAsync(HttpContext context, IMyLibraryService library)
    {
        if (context.Items[Key] is Task<MyLibraryDto> read)
            return read;

        read = library.GetAsync(context.RequestAborted);
        context.Items[Key] = read;
        return read;
    }
}
