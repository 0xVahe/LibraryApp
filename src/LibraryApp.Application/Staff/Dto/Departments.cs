namespace LibraryApp.Application.Staff.Dto;

/// <summary>A department of the bank (MEM-06).</summary>
public sealed record DepartmentDto(Guid Id, string Name);

/// <summary>Creates or renames a department (MEM-06).</summary>
/// <param name="Name">Required, up to 100 characters after trimming. Unique, ignoring case.</param>
public sealed record UpsertDepartmentRequest(string? Name);