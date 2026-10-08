using FluentValidation;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Validators;

public sealed class AddCopyRequestValidator : AbstractValidator<AddCopyRequest>
{
    public AddCopyRequestValidator()
    {
        RuleFor(x => x.Barcode)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(BarcodeRules.IsValid).WithMessage(BarcodeRules.Message);

        RuleFor(x => x.Condition).IsInEnum();

        RuleFor(x => x.ShelfLocation).MaximumLength(BookCopy.ShelfLocationMaxLength);
    }
}
