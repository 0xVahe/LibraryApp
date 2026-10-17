namespace LibraryApp.Application.Reporting.Dto;

public sealed record TopBooksAndCategoriesReportDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTime GeneratedAtUtc,
    IReadOnlyList<TopBookReportItemDto> TopBooks,
    IReadOnlyList<TopCategoryReportItemDto> TopCategories);

public sealed record TopBookReportItemDto(Guid BookId, string Title, int LoanCount);

public sealed record TopCategoryReportItemDto(Guid CategoryId, string Name, int LoanCount);
