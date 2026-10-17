namespace LibraryApp.Application.Borrowing.Dto;

public sealed record ReturnLoanRequest(
    bool Damaged,
    string? Note = null);