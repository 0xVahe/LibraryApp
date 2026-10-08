using FluentValidation;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Validators;

public sealed class AuthorRequestValidator : AbstractValidator<AuthorRequest>
{
    public AuthorRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(Author.FullNameMaxLength);
    }
}
