using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Catalog.Dto;
using LibraryApp.Application.Catalog.Ports;
using LibraryApp.Domain.Catalog;
using LibraryApp.Domain.Common;

namespace LibraryApp.Application.Catalog.Services;

public sealed class CategoryService(
    ICategoryRepository repository, IUnitOfWork unitOfWork, IIdGenerator ids, IApplicationLock applicationLock) : ICategoryService
{
    public Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken ct = default) => repository.ListAsync(ct);

    public async Task<CategoryDto> GetAsync(Guid id, CancellationToken ct = default) => ToDto(await FindAsync(id, ct));

    public Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await applicationLock.AcquireAsync("catalog:categories", token);
            var category = new Category(ids.NewId(), request.Name!);
            category.SetParent(await ParentAsync(request.ParentCategoryId, token));
            repository.Add(category);
            return ToDto(category);
        }, ct);

    public Task UpdateAsync(Guid id, CategoryRequest request, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Serializes hierarchy changes so concurrent reparenting cannot create a third level or cycle.
            await applicationLock.AcquireAsync("catalog:categories", token);
            var category = await FindAsync(id, token);
            category.SetParent(await ParentAsync(request.ParentCategoryId, token));
            category.Rename(request.Name!);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await applicationLock.AcquireAsync("catalog:categories", token);
            var category = await FindAsync(id, token);
            if (category.Children.Count > 0 || await repository.HasBooksAsync(id, token))
                throw new DomainException(CatalogErrors.CategoryInUse);
            repository.Remove(category);
        }, ct);

    private async Task<Category?> ParentAsync(Guid? id, CancellationToken ct) =>
        id.HasValue ? await FindAsync(id.Value, ct) : null;

    private async Task<Category> FindAsync(Guid id, CancellationToken ct) =>
        await repository.FindAsync(id, ct) ?? throw new DomainException(CatalogErrors.CategoryNotFound);

    private static CategoryDto ToDto(Category category) => new(category.Id, category.Name, category.ParentCategoryId);
}
