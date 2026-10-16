using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Staff;
using LibraryApp.Infrastructure.Identity;
using LibraryApp.Infrastructure.Persistence;
using LibraryApp.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LibraryApp.Infrastructure.IntegrationTests.Catalog;

public class BookSuggestionModelTests
{
    [Fact]
    public void Discovered_configuration_preserves_catalog_and_platform_conventions()
    {
        using var db = new LibraryDbContext(new DbContextOptionsBuilder<LibraryDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options);
        var entity = db.Model.FindEntityType(typeof(BookSuggestion))!;
        Assert.Equal("BookSuggestions", entity.GetTableName());
        Assert.Equal(ValueGenerated.Never, entity.FindProperty(nameof(BookSuggestion.Id))!.ValueGenerated);
        Assert.Equal(300, entity.FindProperty(nameof(BookSuggestion.Title))!.GetMaxLength());
        Assert.Equal(200, entity.FindProperty(nameof(BookSuggestion.AuthorName))!.GetMaxLength());
        Assert.Equal(13, entity.FindProperty(nameof(BookSuggestion.Isbn))!.GetMaxLength());
        Assert.Equal(1000, entity.FindProperty(nameof(BookSuggestion.Reason))!.GetMaxLength());
        Assert.Equal(1000, entity.FindProperty(nameof(BookSuggestion.ReviewComment))!.GetMaxLength());
        Assert.Equal("int", entity.FindProperty(nameof(BookSuggestion.Status))!.GetColumnType());
        foreach (var name in new[] { nameof(BookSuggestion.CreatedAtUtc), nameof(BookSuggestion.ReviewedAtUtc) })
            Assert.IsType<UtcDateTimeConverter>(entity.FindProperty(name)!.GetValueConverter());
        foreach (var name in new[] { nameof(BookSuggestion.Isbn), nameof(BookSuggestion.ReviewComment),
                     nameof(BookSuggestion.ReviewedAtUtc), nameof(BookSuggestion.ReviewedByUserId) })
            Assert.True(entity.FindProperty(name)!.IsNullable);
        foreach (var name in new[] { nameof(BookSuggestion.Title), nameof(BookSuggestion.AuthorName), nameof(BookSuggestion.Reason) })
            Assert.False(entity.FindProperty(name)!.IsNullable);
        Assert.Equal(new[] { typeof(AppUser), typeof(StaffMember) }.OrderBy(x => x.Name),
            entity.GetForeignKeys().Select(x => x.PrincipalEntityType.ClrType).OrderBy(x => x.Name));
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Contains(entity.GetIndexes(), x => x.GetDatabaseName() == "IX_BookSuggestions_StaffMemberId_CreatedAtUtc_Id");
        Assert.Contains(entity.GetIndexes(), x => x.GetDatabaseName() == "IX_BookSuggestions_Status_CreatedAtUtc_Id");
    }
}
