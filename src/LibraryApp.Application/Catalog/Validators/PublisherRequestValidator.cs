using FluentValidation;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Domain.Catalog;

namespace LibraryApp.Application.Catalog.Validators;

public sealed class PublisherRequestValidator : AbstractValidator<PublisherRequest>
{
    public PublisherRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Publisher.NameMaxLength);
    }
}
