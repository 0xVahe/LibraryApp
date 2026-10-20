using FluentValidation;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Validators;

public sealed class UpdateBookRequestValidator : AbstractValidator<UpdateBookRequest>
{
    public UpdateBookRequestValidator(IClock clock)
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(Book.TitleMaxLength);

        RuleFor(x => x.Isbn)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(IsbnRules.IsValid)
            .WithMessage(IsbnRules.Message);

        RuleFor(x => x.PublicationYear)
            .InclusiveBetween(1, clock.LocalToday.Year);

        RuleFor(x => x.Language)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Matches("^[a-zA-Z]{2}$")
            .WithMessage("Language must be a two-letter ISO 639-1 code, for example 'en'.");

        RuleFor(x => x.CategoryId)
            .NotEmpty();

        RuleFor(x => x.PublisherId)
            .NotEqual(Guid.Empty);

        // Only the format, also when covers are off (empty Catalog:Covers:BaseUrl): the Edit book form and a PUT send the
        // saved path back, so refusing it would block every edit of a book with a cover. A new book is still refused a
        // cover path while covers are off (CreateBookRequestValidator).
        RuleFor(x => x.CoverImagePath)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(Book.CoverImagePathMaxLength)
            .Must(path => CoverImagePathRules.IsValid(path))
            .WithMessage(CoverImagePathRules.Message);
    }
}
