namespace LibraryApp.Application.Catalog.Dto;

/// <summary>Creates or renames a publisher.</summary>
/// <param name="Name">Required, up to 200 characters.</param>
public sealed record PublisherRequest(string? Name);
