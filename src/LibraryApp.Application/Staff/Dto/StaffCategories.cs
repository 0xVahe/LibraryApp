namespace LibraryApp.Application.Staff.Dto;

public sealed record UpsertStaffCategoryRequest(
    string Name,
    int MaxLoans,
    int LoanDays,
    int MaxRenewals);

public sealed record StaffCategoryDto(
    Guid Id,
    string Name,
    int MaxLoans,
    int LoanDays,
    int MaxRenewals);