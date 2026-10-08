using FluentValidation;
using LibraryApp.Application.Catalog.Dto;

namespace LibraryApp.Application.Catalog.Validators;

/// <summary>Only checks that the status is a known value; which statuses a librarian may set is a business rule (service).</summary>
public sealed class ChangeCopyStatusRequestValidator : AbstractValidator<ChangeCopyStatusRequest>
{
    public ChangeCopyStatusRequestValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
    }
}
