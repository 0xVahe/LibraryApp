using System.Globalization;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Services;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Persistence.Seed.Catalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LibraryApp.Infrastructure.IntegrationTests.Persistence;

public class CatalogSeedDataTests
{
    // Independent snapshots lock the reference implementation's mappings for downstream book seeds.
    [Fact]
    public void Authors_keep_their_fixed_name_to_id_mapping() => AssertMapping(
        CatalogSeedData.Authors, "0002", """
            Benjamin Graham
            Richard A. Brealey
            Stewart C. Myers
            Franklin Allen
            John C. Hull
            Niall Ferguson
            Liaquat Ahamed
            Anat Admati
            Martin Hellwig
            Michael Lewis
            Peter L. Bernstein
            Nassim Nicholas Taleb
            Philippe Jorion
            Roger Lowenstein
            Tom Burgis
            Oliver Bullough
            Robert C. Martin
            David Thomas
            Andrew Hunt
            Erich Gamma
            Richard Helm
            Ralph Johnson
            John Vlissides
            Martin Fowler
            Eric Evans
            Jon Skeet
            Martin Kleppmann
            Cole Nussbaumer Knaflic
            Wes McKinney
            Foster Provost
            Tom Fawcett
            Nate Silver
            Ralph Kimball
            Margy Ross
            Charles Wheelan
            Jim Collins
            Patrick Lencioni
            Andrew S. Grove
            Kim Scott
            Camille Fournier
            Simon Sinek
            Daniel H. Pink
            Dale Carnegie
            Kerry Patterson
            Joseph Grenny
            Ron McMillan
            Al Switzler
            Chris Voss
            Tahl Raz
            Roger Fisher
            William Ury
            Bruce Patton
            Максим Ильяхов
            Людмила Сарычева
            Максим Дорофеев
            Matthew Dixon
            Nick Toman
            Rick DeLisi
            Fred Reichheld
            Rob Markey
            Tony Hsieh
            Danny Meyer
            Jay Baer
            Harley Manning
            Kerry Bodine
            B. Joseph Pine II
            James H. Gilmore
            Yuval Noah Harari
            Daniel Kahneman
            James Clear
            Михаил Булгаков
            Րաֆֆի
            Հովհաննես Թումանյան
            Եղիշե Չարենց
            Հրանտ Մաթևոսյան
            """, Author.FullNameMaxLength);

    [Fact]
    public void Publishers_keep_their_fixed_name_to_id_mapping() => AssertMapping(
        CatalogSeedData.Publishers, "0003", """
            Harper Business
            McGraw Hill
            Pearson
            Penguin Press
            Princeton University Press
            W. W. Norton & Company
            Wiley
            Random House
            Harper
            Profile Books
            Prentice Hall
            Addison-Wesley
            Manning
            O'Reilly Media
            Jossey-Bass
            St. Martin's Press
            Portfolio
            Riverhead Books
            Simon & Schuster
            Penguin Books
            Альпина Паблишер
            Манн, Иванов и Фербер
            Harvard Business Review Press
            Grand Central Publishing
            New Harvest
            Farrar, Straus and Giroux
            Avery
            """, Publisher.NameMaxLength);

    [Fact]
    public void Categories_keep_their_existing_fixed_mapping() => AssertMapping(
        CatalogSeedData.Categories, "0001", """
            Banking & Finance
            Risk, Compliance & AML
            IT & Software Engineering
            Data & Analytics
            Leadership & Management
            Soft Skills & Communication
            Customer Experience
            General Reading
            """, Category.NameMaxLength);

    [Fact]
    public void Book_ids_keep_their_fixed_scheme()
    {
        // The copies seed and the loans seed reference these ids, so they must never change.
        Assert.Equal(new Guid("b0000000-0000-0000-0000-000000000001"), CatalogSeedIds.Book(1));
        Assert.Equal(new Guid("b0000000-0000-0000-0000-000000000057"), CatalogSeedIds.Book(57));
        Assert.Equal(Enumerable.Range(1, CatalogSeedData.Books.Count),
            CatalogSeedData.Books.Select(book => book.Number));
    }

    [Fact]
    public void Copy_ids_and_barcodes_keep_their_fixed_scheme()
    {
        // The loans seed references these values, so they must never change.
        Assert.Equal(new Guid("c0000000-0000-0000-0000-000000001001"), CatalogSeedIds.Copy(1, 1));
        Assert.Equal(new Guid("c0000000-0000-0000-0000-000000057001"), CatalogSeedIds.Copy(57, 1));
        Assert.Equal("BK-0001-01", CatalogSeedIds.CopyBarcode(1, 1));
        Assert.Equal("BK-0057-01", CatalogSeedIds.CopyBarcode(57, 1));
    }

    [Fact]
    public void Every_seeded_book_has_one_to_four_copies_with_unique_valid_barcodes()
    {
        Assert.Equal(CatalogSeedData.Books.Select(book => book.Number).Order(), CatalogSeedData.CopiesPerBook.Keys.Order());
        Assert.All(CatalogSeedData.CopiesPerBook.Values, count => Assert.InRange(count, 1, 4));
        Assert.Equal(115, CatalogSeedData.CopiesPerBook.Values.Sum());

        var copies = CatalogSeedData.CopiesPerBook
            .SelectMany(entry => Enumerable.Range(1, entry.Value).Select(copy => (Book: entry.Key, Copy: copy)))
            .ToArray();
        var ids = copies.Select(c => CatalogSeedIds.Copy(c.Book, c.Copy)).ToArray();
        var barcodes = copies.Select(c => CatalogSeedIds.CopyBarcode(c.Book, c.Copy)).ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(barcodes.Length, barcodes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(barcodes, barcode => Assert.Matches($"^[A-Z0-9-]{{1,{BookCopy.BarcodeMaxLength}}}$", barcode));
        Assert.Empty(ids.Intersect(CatalogSeedData.Books.Select(book => CatalogSeedIds.Book(book.Number))));
    }

    [Fact]
    public void Reference_ids_are_unique_across_all_seed_sets()
    {
        var ids = CatalogSeedData.Categories.Concat(CatalogSeedData.Authors)
            .Concat(CatalogSeedData.Publishers).Select(x => x.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.DoesNotContain(CatalogSeedData.Publishers,
            x => string.Equals(x.Name, "No publisher", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Module_registration_keeps_other_seeders_and_catalog_services()
    {
        var services = new ServiceCollection();
        var otherSeeder = new OtherSeeder();
        services.AddSingleton<IModuleSeeder>(otherSeeder);
        services.AddCatalogModule(new ConfigurationBuilder().Build());

        var registration = Assert.Single(services, x => x.ImplementationType == typeof(CatalogSeeder));
        Assert.Equal(typeof(IModuleSeeder), registration.ServiceType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Contains(services, x => ReferenceEquals(x.ImplementationInstance, otherSeeder));
        Assert.Contains(services, x => x.ServiceType == typeof(IAuthorService));
        Assert.Contains(services, x => x.ServiceType == typeof(ICategoryService));
        Assert.Contains(services, x => x.ServiceType == typeof(IBookSearchService));
        Assert.Contains(services, x => x.ServiceType == typeof(IBookCopyService));
    }

    private static void AssertMapping(
        IReadOnlyList<(Guid Id, string Name)> actual, string segment, string snapshot, int maxNameLength)
    {
        var expectedNames = snapshot.Split('\n', StringSplitOptions.TrimEntries);
        Assert.Equal(expectedNames.Length, actual.Count);
        Assert.Equal(actual.Count, actual.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        for (var index = 0; index < expectedNames.Length; index++)
        {
            var expectedId = Guid.Parse("10000000-0000-0000-" + segment + "-"
                + (index + 1).ToString("D12", CultureInfo.InvariantCulture));
            Assert.Equal(expectedId, actual[index].Id);
            Assert.Equal(expectedNames[index], actual[index].Name);
            Assert.InRange(actual[index].Name.Length, 1, maxNameLength);
        }
    }

    private sealed class OtherSeeder : IModuleSeeder
    {
        public int Order => 20;
        public string Module => "Other";
        public Task SeedAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
