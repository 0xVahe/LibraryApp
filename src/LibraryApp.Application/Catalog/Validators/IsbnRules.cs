namespace LibraryApp.Application.Catalog.Validators;

internal static class IsbnRules
{
    public const string Message =
        "ISBN must be a valid ISBN-10 or ISBN-13.";

    public static bool IsValid(string? isbn) =>
        TryNormalize(isbn, out _);

    public static bool TryNormalize(string? isbn, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(isbn))
            return false;

        var value = new string(
            isbn.Where(c => c != '-' && c != ' ').ToArray());

        if (value.Length == 10 && IsValidIsbn10(value))
        {
            var firstTwelve = "978" + value[..9];
            normalized = firstTwelve + CalculateIsbn13CheckDigit(firstTwelve);
            return true;
        }

        if (value.Length == 13 && value.All(char.IsAsciiDigit)
            && IsValidIsbn13(value))
        {
            normalized = value;
            return true;
        }

        return false;
    }

    private static bool IsValidIsbn10(string isbn)
    {
        var sum = 0;

        for (var i = 0; i < 10; i++)
        {
            var c = isbn[i];

            int digit;

            if (i == 9 && c is 'X' or 'x')
                digit = 10;
            else if (char.IsAsciiDigit(c))
                digit = c - '0';
            else
                return false;

            sum += digit * (10 - i);
        }

        return sum % 11 == 0;
    }

    private static bool IsValidIsbn13(string isbn)
    {
        if (!isbn.All(char.IsAsciiDigit))
            return false;

        var sum = 0;

        for (var i = 0; i < 13; i++)
        {
            var digit = isbn[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }

        return sum % 10 == 0;
    }

    private static int CalculateIsbn13CheckDigit(string firstTwelve)
    {
        var sum = 0;

        for (var i = 0; i < 12; i++)
        {
            var digit = firstTwelve[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }

        return (10 - sum % 10) % 10;
    }
}
