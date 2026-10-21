namespace LibraryApp.Application.Borrowing.Dto;

/// <summary>Taking a copy back (BOR-03): whether it came back damaged and an optional note.</summary>
/// <param name="Damaged">True when the copy came back damaged: the damaged points are added and the copy goes to repair (BOR-09).</param>
/// <param name="Note">Optional note about the return, at most 500 characters; spaces around it are removed.</param>
public sealed record ReturnLoanRequest(
    bool Damaged,
    string? Note = null);
