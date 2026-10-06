using LibraryApp.Domain.Common;

namespace LibraryApp.Domain.Catalog;

/// <summary>One physical copy of a book. Identified by its barcode (BR-CAT-2: unique across ALL copies).</summary>
public sealed class BookCopy : Entity
{
    public const int BarcodeMaxLength = 32;
    public const int ShelfLocationMaxLength = 50;

    private BookCopy() { }                           

    /// <summary>A new copy always starts as Available.</summary>
    public BookCopy(Guid id, Guid bookId, string barcode, BookCopyCondition condition, string? shelfLocation)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            throw new DomainException(CatalogErrors.CopyBarcodeRequired);

        EnsureConditionIsKnown(condition);

        BookId = bookId;
        Barcode = NormalizeBarcode(barcode);
        Status = BookCopyStatus.Available;
        Condition = condition;
        ShelfLocation = NormalizeShelfLocation(shelfLocation);
    }

    public Guid BookId { get; private set; }

    /// <summary>Trimmed and upper case (G1-06). The format (A-Z, 0-9, '-', up to 32) is checked by the validator.</summary>
    public string Barcode { get; private set; } = null!;

    public BookCopyStatus Status { get; private set; }

    public BookCopyCondition Condition { get; private set; }

    public string? ShelfLocation { get; private set; }

    /// <summary>Concurrency token filled by the database: two changes of one copy at the same time give a 409.</summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public void UpdateDetails(BookCopyCondition condition, string? shelfLocation)
    {
        EnsureConditionIsKnown(condition);

        Condition = condition;
        ShelfLocation = NormalizeShelfLocation(shelfLocation);
    }

    public bool CanChangeStatusTo(BookCopyStatus newStatus)
    {
        return BookCopyStatusTransitions.IsAllowed(Status, newStatus);
    }

    /// <summary>Changes the status if the transition is allowed, otherwise throws and keeps the old status.</summary>
    public void ChangeStatus(BookCopyStatus newStatus)
    {
        if (!CanChangeStatusTo(newStatus))
            throw new DomainException(CatalogErrors.CopyStatusChangeNotAllowed(Status, newStatus));

        Status = newStatus;
    }

    private static string NormalizeBarcode(string barcode)
    {
        return barcode.Trim().ToUpperInvariant();
    }

    private static string? NormalizeShelfLocation(string? shelfLocation)
    {
        return string.IsNullOrWhiteSpace(shelfLocation) ? null : shelfLocation.Trim();
    }

    private static void EnsureConditionIsKnown(BookCopyCondition condition)
    {
        if (!Enum.IsDefined(condition))
            throw new DomainException(CatalogErrors.CopyConditionUnknown);
    }
}
