using FluentValidation;
using LibraryApp.Application.Staff.Dto;

namespace LibraryApp.Application.Staff.Validators;

public sealed class UpsertDepartmentRequestValidator : AbstractValidator<UpsertDepartmentRequest>
{
    public const int NameMaxLength = 100;

    public UpsertDepartmentRequestValidator()
    {
        // Checked after trimming, the way Department stores it: spaces only is empty, padding does not count.
        Transform(x => x.Name, name => name?.Trim()).NotEmpty().MaximumLength(NameMaxLength);
    }
}