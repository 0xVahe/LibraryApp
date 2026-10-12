using FluentValidation;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Validators;

public sealed class UpdateCopyRequestValidator : AbstractValidator<UpdateCopyRequest>
{
    public UpdateCopyRequestValidator()
    {
        RuleFor(x => x.Condition).IsInEnum();

        RuleFor(x => x.ShelfLocation).MaximumLength(BookCopy.ShelfLocationMaxLength);
    }
}
