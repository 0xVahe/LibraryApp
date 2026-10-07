namespace LibraryApp.Application.Abstractions.Staff;

/// <summary>Lending limits of one staff member (INT-04): the category limits, reduced when Restricted (BR-MEM-3).</summary>
public sealed record LendingRules(int MaxLoans, int LoanDays, int MaxRenewals);