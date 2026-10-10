using FluentValidation;
using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Validators;

public sealed class UpsertDepartmentRequestValidator : AbstractValidator<UpsertDepartmentRequest>
{
    public const int NameMaxLength = 100;

    public UpsertDepartmentRequestValidator()
    {
        // Checked after trimming, the way Department stores it: spaces only is empty, padding does not count.
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Name is required.")
            .Must(name => name!.Trim().Length <= NameMaxLength)
            .WithMessage($"Name must be {NameMaxLength} characters or fewer.");
    }
}