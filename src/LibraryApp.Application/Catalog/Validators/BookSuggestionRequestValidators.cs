using FluentValidation;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Validators;

public sealed class CreateBookSuggestionRequestValidator : AbstractValidator<CreateBookSuggestionRequest>
{
    public CreateBookSuggestionRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Book.TitleMaxLength);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(Author.FullNameMaxLength);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(BookSuggestion.ReasonMaxLength);
        RuleFor(x => x.Isbn).Must(isbn => isbn is null || IsbnRules.IsValid(isbn))
            .WithMessage(IsbnRules.Message);
    }
}

public sealed class RejectBookSuggestionRequestValidator : AbstractValidator<RejectBookSuggestionRequest>
{
    public RejectBookSuggestionRequestValidator() =>
        RuleFor(x => x.Comment).NotEmpty().MaximumLength(BookSuggestion.ReviewCommentMaxLength);
}

public sealed class BookSuggestionListRequestValidator : AbstractValidator<BookSuggestionListRequest>
{
    public BookSuggestionListRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}
