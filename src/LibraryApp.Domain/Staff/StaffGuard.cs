using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Staff;

/// <summary>Small checks shared by the entities of the Staff module.</summary>
internal static class StaffGuard
{
    /// <summary>Returns the trimmed text, or throws <see cref="DomainException"/> when it is empty.</summary>
    public static string RequireText(string? value, Error error)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(error);

        return value.Trim();
    }

    public static Guid RequireId(Guid value, Error error) =>
        value == Guid.Empty ? throw new DomainException(error) : value;
}