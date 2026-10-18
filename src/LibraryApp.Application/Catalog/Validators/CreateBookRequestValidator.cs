using FluentValidation;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Options;
using LibraryApp.Domain.Catalog;
using Microsoft.Extensions.Options;

namespace LibraryApp.Application.Catalog.Validators;

public sealed class CreateBookRequestValidator : AbstractValidator<CreateBookRequest>
{
    public CreateBookRequestValidator(IClock clock, IOptions<CatalogCoversOptions>? covers = null)
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

        RuleFor(x => x.CoverImagePath)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(Book.CoverImagePathMaxLength)
            .Must(path => CoverImagePathRules.IsValid(path, !string.IsNullOrEmpty(covers?.Value.BaseUrl)))
            .WithMessage(CoverImagePathRules.Message);

        RuleFor(x => x.AuthorIds)
            .Cascade(CascadeMode.Stop)
            .NotEmpty();

        RuleForEach(x => x.AuthorIds)
            .NotEmpty();
    }
}
