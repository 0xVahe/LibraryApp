using FluentValidation;
using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Validators;

public sealed class BookAuthorsRequestValidator : AbstractValidator<BookAuthorsRequest>
{
    public BookAuthorsRequestValidator()
    {
        RuleFor(x => x.AuthorIds).NotEmpty();
        RuleForEach(x => x.AuthorIds).NotEmpty();
    }
}
