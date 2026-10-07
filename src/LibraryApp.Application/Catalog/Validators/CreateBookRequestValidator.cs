using FluentValidation;
using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Validators;

public sealed class CreateBookRequestValidator : AbstractValidator<CreateBookRequest>
{
    public CreateBookRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.Isbn)
            .NotEmpty()
            .Must(IsValidIsbn)
            .WithMessage("ISBN must be a valid ISBN-10 or ISBN-13.");

        RuleFor(x => x.PublicationYear)
            .InclusiveBetween(1, DateTime.UtcNow.Year);

        RuleFor(x => x.Language)
            .NotEmpty()
            .Length(2)
            .Matches("^[a-zA-Z]{2}$");

        RuleFor(x => x.CategoryId)
            .NotEmpty();

        RuleFor(x => x.AuthorIds)
            .NotEmpty()
            .Must(authors => authors.Distinct().Count() == authors.Count)
            .WithMessage("A book must have at least one unique author.");

        RuleForEach(x => x.AuthorIds)
            .NotEmpty();
    }

    private static bool IsValidIsbn(string isbn)
    {
        var value = isbn.Trim();

        return IsValidIsbn10(value) || IsValidIsbn13(value);
    }

    private static bool IsValidIsbn10(string isbn)
    {
        if (isbn.Length != 10)
            return false;

        var sum = 0;

        for (var i = 0; i < 10; i++)
        {
            var value = isbn[i];

            var digit = i == 9 && value is 'X' or 'x'
                ? 10
                : char.IsDigit(value)
                    ? value - '0'
                    : -1;

            if (digit < 0)
                return false;

            sum += digit * (10 - i);
        }

        return sum % 11 == 0;
    }

    private static bool IsValidIsbn13(string isbn)
    {
        if (isbn.Length != 13 || !isbn.All(char.IsDigit))
            return false;

        var sum = 0;

        for (var i = 0; i < 13; i++)
        {
            var digit = isbn[i] - '0';

            sum += i % 2 == 0
                ? digit
                : digit * 3;
        }

        return sum % 10 == 0;
    }
}