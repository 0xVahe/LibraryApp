using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Validators;

/// <summary>Barcode format (G1-06): letters, digits and '-', up to 32 characters after trimming. Case does not matter.</summary>
internal static class BarcodeRules
{
    public static readonly string Message =
        $"The barcode may contain only letters, digits and '-', up to {BookCopy.BarcodeMaxLength} characters.";

    public static bool IsValid(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return false;

        string value = barcode.Trim();
        if (value.Length > BookCopy.BarcodeMaxLength)
            return false;

        return value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }
}
