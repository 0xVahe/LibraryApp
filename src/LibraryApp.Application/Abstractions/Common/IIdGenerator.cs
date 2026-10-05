namespace LibraryApp.Application.Abstractions.Common;

/// <summary>Creates identifiers before the row is saved (SD-01: Guid, in an order that suits SQL Server indexes).</summary>
public interface IIdGenerator
{
    Guid NewId();
}
