using FluentValidation;
using LibraryApp.Application.Borrowing.Dto;
using LibraryApp.Domain.Borrowing;

namespace LibraryApp.Application.Borrowing.Validators;

/// <summary>
/// Validates return input before the return transaction starts.
/// </summary>
public sealed class ReturnLoanRequestValidator
    : AbstractValidator<ReturnLoanRequest>
{
    public ReturnLoanRequestValidator()
    {
        RuleFor(x => x.Note)
            .Must(note =>
                note is null
                || note.Trim().Length <= Loan.ReturnNoteMaxLength)
            .WithMessage(
                $"The return note may have at most " +
                $"{Loan.ReturnNoteMaxLength} characters.");
    }
}