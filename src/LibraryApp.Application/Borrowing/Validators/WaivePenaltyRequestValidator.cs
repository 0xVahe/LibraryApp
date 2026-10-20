using FluentValidation;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Validators;

/// <summary>BOR-08: a waiver needs a reason (400 without one), at most the length the record stores.</summary>
public sealed class WaivePenaltyRequestValidator : AbstractValidator<WaivePenaltyRequest>
{
    public WaivePenaltyRequestValidator()
    {
        RuleFor(x => x.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("A reason is required to waive the penalty.")
            .Must(reason => reason is null || reason.Trim().Length <= PenaltyRecord.ReasonMaxLength)
            .WithMessage($"The reason may have at most {PenaltyRecord.ReasonMaxLength} characters.");
    }
}
