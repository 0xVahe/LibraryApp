using System.Security.Claims;
using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Domain.Auditing;
using LibraryApp.Domain.Catalog;
using LibraryApp.Infrastructure.Extensions;
using LibraryApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LibraryApp.Infrastructure.IntegrationTests.Auditing;

/// <summary>The platform as the API registers it (with the audit interceptor), a fixed clock, a throw-away database.</summary>
public sealed class AuditFixture : IAsyncLifetime
{
    public static readonly DateTime Now = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:LibraryDb"] = TestDatabase.NewConnectionString(),
            ["Messaging:DispatcherEnabled"] = "false",
            ["Time:TimeZoneId"] = "Asia/Yerevan",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatform(configuration, new TestEnvironment());
        services.AddSingleton<TimeProvider>(new FixedTime(Now));
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.EnsureDeletedAsync();
        await Services.DisposeAsync();
    }

    /// <summary>Runs the change in one unit of work, as a request of <paramref name="userId"/> or, when null, without a user.</summary>
    public async Task InTransactionAsync(Guid? userId, Func<LibraryDbContext, Task> change)
    {
        var accessor = Services.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = userId is null ? null : new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.Value.ToString())], "Bearer")),
        };
        try
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ => change(db));
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    public async Task<List<AuditLog>> RowsAsync(Guid entityId)
    {
        using var scope = Services.CreateScope();
        string id = entityId.ToString();
        return await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().AuditLogs.AsNoTracking()
            .Where(a => a.EntityId == id).OrderBy(a => a.Id).ToListAsync();
    }

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "LibraryApp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

/// <summary>NFR-14: changes of books, staff, loans and penalties are recorded with the user, in the same transaction.</summary>
public class AuditLogTests(AuditFixture fixture) : IClassFixture<AuditFixture>
{
    [Fact]
    public async Task Creating_updating_and_soft_deleting_a_book_records_each_change_with_the_user_and_time()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();

        await fixture.InTransactionAsync(userId, db =>
        {
            var category = new Category(Guid.NewGuid(), $"Audit {bookId:N}");
            db.Categories.Add(category);
            db.Books.Add(new Book(bookId, "Audited Book", Isbn(bookId), 2020, "en", null, category.Id, null));
            return Task.CompletedTask;
        });
        await fixture.InTransactionAsync(userId, async db =>
        {
            Book book = await db.Books.SingleAsync(b => b.Id == bookId);
            book.Update("Audited Book, 2nd edition", book.Isbn, 2021, book.Language, null, book.CategoryId, null);
        });
        await fixture.InTransactionAsync(userId, async db =>
            (await db.Books.SingleAsync(b => b.Id == bookId)).Delete(AuditFixture.Now));

        // A soft delete (BR-CAT-3) is the delete action NFR-14 names.
        List<AuditLog> rows = await fixture.RowsAsync(bookId);
        Assert.Equal([AuditActions.Created, AuditActions.Updated, AuditActions.Deleted], rows.Select(r => r.Action));
        Assert.All(rows, row =>
        {
            Assert.Equal("Book", row.EntityName);
            Assert.Equal(userId, row.UserId);
            Assert.Equal(AuditFixture.Now, row.TimestampUtc);
        });
    }

    [Fact]
    public async Task Changing_the_authors_of_a_book_records_the_book_once_and_a_new_book_with_authors_only_as_created()
    {
        var userId = Guid.NewGuid();
        var bookId = Guid.NewGuid();
        var first = new Author(Guid.NewGuid(), $"First {bookId:N}");
        var second = new Author(Guid.NewGuid(), $"Second {bookId:N}");
        var third = new Author(Guid.NewGuid(), $"Third {bookId:N}");

        // Created together with its authors: one Created row, no extra Updated.
        await fixture.InTransactionAsync(userId, db =>
        {
            var category = new Category(Guid.NewGuid(), $"Authors {bookId:N}");
            db.Categories.Add(category);
            db.Authors.AddRange(first, second, third);
            db.Books.Add(new Book(bookId, "Book With Authors", Isbn(bookId), 2020, "en", null, category.Id, null));
            db.BookAuthors.Add(new BookAuthor(bookId, first.Id));
            return Task.CompletedTask;
        });

        // CAT-02: only the links change (one removed, two added) — the book row itself stays Unchanged.
        await fixture.InTransactionAsync(userId, async db =>
        {
            db.BookAuthors.Remove(await db.BookAuthors.SingleAsync(l => l.BookId == bookId && l.AuthorId == first.Id));
            db.BookAuthors.AddRange(new BookAuthor(bookId, second.Id), new BookAuthor(bookId, third.Id));
        });

        List<AuditLog> rows = await fixture.RowsAsync(bookId);
        Assert.Equal([AuditActions.Created, AuditActions.Updated], rows.Select(r => r.Action));
        Assert.All(rows, row => Assert.Equal(("Book", userId), (row.EntityName, row.UserId)));
    }

    [Fact]
    public async Task Entities_outside_NFR_14_are_not_recorded()
    {
        var categoryId = Guid.NewGuid();

        await fixture.InTransactionAsync(Guid.NewGuid(), db =>
        {
            db.Categories.Add(new Category(categoryId, $"Not audited {categoryId:N}"));
            return Task.CompletedTask;
        });

        Assert.Empty(await fixture.RowsAsync(categoryId));
    }

    [Fact]
    public async Task A_rolled_back_change_leaves_no_audit_row()
    {
        var bookId = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InTransactionAsync(Guid.NewGuid(), async db =>
        {
            var category = new Category(Guid.NewGuid(), $"Rollback {bookId:N}");
            db.Categories.Add(category);
            db.Books.Add(new Book(bookId, "Never Saved", Isbn(bookId), 2020, "en", null, category.Id, null));
            await db.SaveChangesAsync();                                 // written inside the transaction ...
            throw new InvalidOperationException("the use case fails");   // ... and rolled back with it
        }));

        Assert.Empty(await fixture.RowsAsync(bookId));
    }

    [Fact]
    public async Task Without_a_signed_in_user_the_change_is_recorded_as_system()
    {
        var bookId = Guid.NewGuid();

        await fixture.InTransactionAsync(null, db =>
        {
            var category = new Category(Guid.NewGuid(), $"System {bookId:N}");
            db.Categories.Add(category);
            db.Books.Add(new Book(bookId, "Seeded Book", Isbn(bookId), 2020, "en", null, category.Id, null));
            return Task.CompletedTask;
        });

        Assert.Null(Assert.Single(await fixture.RowsAsync(bookId)).UserId);
    }

    /// <summary>A valid ISBN-13 that differs per test (978 + 9 digits + check digit).</summary>
    private static string Isbn(Guid seed)
    {
        string digits = "978" + (Math.Abs(seed.GetHashCode()) % 1_000_000_000).ToString("D9");
        int sum = digits.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return digits + (10 - sum % 10) % 10;
    }
}
