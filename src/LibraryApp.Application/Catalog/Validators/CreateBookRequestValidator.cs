using FluentValidation;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Validators;

public sealed class CreateBookRequestValidator : AbstractValidator<CreateBookRequest>
{
    public CreateBookRequestValidator(IClock clock)
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

        RuleFor(x => x.AuthorIds)
            .Cascade(CascadeMode.Stop)
            .NotEmpty();

        RuleForEach(x => x.AuthorIds)
            .NotEmpty();
    }
}
