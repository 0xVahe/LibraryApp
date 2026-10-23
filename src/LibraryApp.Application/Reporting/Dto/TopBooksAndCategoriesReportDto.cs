namespace LibraryApp.Application.Reporting.Dto;

/// <summary>REP-03: the most borrowed books and categories of a period, most loans first.</summary>
/// <param name="PeriodStart">The first day of the period (Asia/Yerevan), included.</param>
/// <param name="PeriodEnd">The last day of the period (Asia/Yerevan), included.</param>
/// <param name="GeneratedAtUtc">When the report was made.</param>
/// <param name="TopBooks">At most <c>Reporting:TopBooksLimit</c> books.</param>
/// <param name="TopCategories">At most <c>Reporting:TopCategoriesLimit</c> categories.</param>
public sealed record TopBooksAndCategoriesReportDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateTime GeneratedAtUtc,
    IReadOnlyList<TopBookReportItemDto> TopBooks,
    IReadOnlyList<TopCategoryReportItemDto> TopCategories);

/// <summary>A book with its loans in the period (REP-03).</summary>
/// <param name="BookId">Book id.</param>
/// <param name="Title">Book title.</param>
/// <param name="LoanCount">Loans issued in the period.</param>
public sealed record TopBookReportItemDto(Guid BookId, string Title, int LoanCount);

/// <summary>A category with its loans in the period (REP-03).</summary>
/// <param name="CategoryId">Category id.</param>
/// <param name="Name">Category name.</param>
/// <param name="LoanCount">Loans issued in the period.</param>
public sealed record TopCategoryReportItemDto(Guid CategoryId, string Name, int LoanCount);
