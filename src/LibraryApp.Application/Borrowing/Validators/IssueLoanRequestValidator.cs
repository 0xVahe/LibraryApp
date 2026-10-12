using FluentValidation;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Borrowing.Validators;

/// <summary>Input of an issue (BOR-01): a staff member id and the scanned barcode. Rules of the loan itself are in the service.</summary>
public sealed class IssueLoanRequestValidator : AbstractValidator<IssueLoanRequest>
{
    public IssueLoanRequestValidator()
    {
        RuleFor(x => x.StaffMemberId).NotEmpty();

        // The barcode as scanned: Catalog ignores case and surrounding spaces (G1-06).
        RuleFor(x => x.Barcode)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(barcode => barcode.Trim().Length <= BookCopy.BarcodeMaxLength)
            .WithMessage($"The barcode may have at most {BookCopy.BarcodeMaxLength} characters.");
    }
}
