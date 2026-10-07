using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace LibraryApp.Infrastructure.Persistence.Repositories.Catalog;

public sealed class CategoryRepository(LibraryDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct) =>
        await db.Categories.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new CategoryDto(x.Id, x.Name, x.ParentCategoryId)).ToListAsync(ct);

    public Task<Category?> FindAsync(Guid id, CancellationToken ct) =>
        db.Categories.Include(x => x.Parent).Include(x => x.Children).SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> HasBooksAsync(Guid id, CancellationToken ct) =>
        db.Books.IgnoreQueryFilters().AnyAsync(x => x.CategoryId == id, ct);

    public void Add(Category category) => db.Categories.Add(category);
    public void Remove(Category category) => db.Categories.Remove(category);
}
