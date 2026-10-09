using LibraryApp.Application.Abstractions.Common;
using LibraryApp.Application.Staff.Dto;
using LibraryApp.Application.Staff.Ports;
using LibraryApp.Application.Staff.Services;
using LibraryApp.Application.Tests.Catalog;
using LibraryApp.Domain.Common;
using LibraryApp.Domain.Staff;

namespace LibraryApp.Application.Tests.Staff;

public class StaffCategoryServiceTests
{
    private readonly FakeCategories _repository = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FixedIds _ids = new();

    private StaffCategoryService Service => new(_repository, _unitOfWork, _ids);

    [Fact]
    public async Task Create_generates_id_trims_name_and_adds_category()
    {
        var result = await Service.CreateAsync(
            new UpsertStaffCategoryRequest("  Contractors  ", 4, 25, 0));

        var expected = new StaffCategoryDto(_ids.Id, "Contractors", 4, 25, 0);

        Assert.Equal(expected, result);

        var entity = Assert.Single(_repository.Categories);
        Assert.Equal(expected, new StaffCategoryDto(entity.Id, entity.Name, entity.MaxLoans, entity.LoanDays, entity.MaxRenewals));

        Assert.Equal(1, _unitOfWork.Transactions);
    }

    [Fact]
    public async Task Get_returns_existing_category()
    {
        var category = new StaffCategory(Guid.NewGuid(), "Employee", 3, 21, 1);
        _repository.Categories.Add(category);

        var result = await Service.GetAsync(category.Id);

        Assert.Equal(
            new StaffCategoryDto(category.Id, "Employee", 3, 21, 1), result);
        Assert.Equal(0, _unitOfWork.Transactions);
    }

    [Fact]
    public async Task Update_changes_existing_category_and_preserves_id()
    {
        var category = new StaffCategory(
            Guid.NewGuid(), "Employee", 3, 21, 1);
        _repository.Categories.Add(category);

        var result = await Service.UpdateAsync(
            category.Id,
            new UpsertStaffCategoryRequest("  Updated  ", 6, 28, 2));

        var expected = new StaffCategoryDto(
            category.Id, "Updated", 6, 28, 2);

        Assert.Equal(expected, result);
        Assert.Equal(expected, await Service.GetAsync(category.Id));
        Assert.Same(category, Assert.Single(_repository.Categories));
        Assert.Equal(1, _unitOfWork.Transactions);
    }

    [Theory]
    [InlineData(false, "Employee")]
    [InlineData(false, "  Employee  ")]
    [InlineData(true, "Employee")]
    [InlineData(true, "  Employee  ")]
    public async Task Duplicate_name_is_rejected_without_changing_categories(
        bool update, string name)
    {
        var existing = new StaffCategory(
            Guid.NewGuid(), "Employee", 3, 21, 1);
        var target = new StaffCategory(
            Guid.NewGuid(), "Contractors", 2, 14, 1);

        _repository.Categories.AddRange([existing, target]);

        var before = await Service.GetAsync(target.Id);
        var request = new UpsertStaffCategoryRequest(name, 6, 28, 2);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            update
                ? Service.UpdateAsync(target.Id, request)
                : Service.CreateAsync(request));

        Assert.Equal(StaffErrors.CategoryNameTaken, error.Error);
        Assert.Equal(2, _repository.Categories.Count);
        Assert.Equal(before, await Service.GetAsync(target.Id));
        Assert.Equal(1, _unitOfWork.Transactions);
    }
    
    [Theory]
    [InlineData("Employee")]
    [InlineData("  Employee  ")]
    public async Task Update_can_keep_its_own_name_and_change_limits(string name)
    {
        var category = new StaffCategory(
            Guid.NewGuid(), "Employee", 3, 21, 1);
        _repository.Categories.Add(category);

        var result = await Service.UpdateAsync(
            category.Id,
            new UpsertStaffCategoryRequest(name, 6, 28, 2));

        var expected = new StaffCategoryDto(
            category.Id, "Employee", 6, 28, 2);

        Assert.Equal(expected, result);
        Assert.Equal(expected, await Service.GetAsync(category.Id));
        Assert.Same(category, Assert.Single(_repository.Categories));
    }
    
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_category_throws_category_not_found(bool update)
    {
        var id = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<DomainException>(async () =>
        {
            if (update)
            {
                await Service.UpdateAsync(
                    id, new UpsertStaffCategoryRequest("Missing", 3, 21, 1));
            }
            else
            {
                await Service.GetAsync(id);
            }
        });

        Assert.Equal(StaffErrors.CategoryResourceNotFound, error.Error);
        Assert.Empty(_repository.Categories);
    }

    [Fact]
    public async Task List_returns_repository_results()
    {
        IReadOnlyList<StaffCategoryDto> expected =
        [
            new(Guid.NewGuid(), "Employee", 3, 21, 1),
            new(Guid.NewGuid(), "Intern / Trainee", 2, 14, 1)
        ];
        _repository.ListResult = expected;

        var result = await Service.GetListAsync();

        Assert.Equal(expected.ToArray(), result.ToArray());
        Assert.Equal(0, _unitOfWork.Transactions);
    }

    [Theory]
    [InlineData(0, 14, 1, "staff.max_loans_invalid")]
    [InlineData(2, 0, 1, "staff.loan_days_invalid")]
    [InlineData(2, 14, -1, "staff.max_renewals_invalid")]
    public async Task Create_rejects_invalid_limits_without_adding_category(
        int maxLoans, int loanDays, int maxRenewals, string code)
    {
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            Service.CreateAsync(new UpsertStaffCategoryRequest(
                "Invalid", maxLoans, loanDays, maxRenewals)));

        Assert.Equal(code, error.Error.Code);
        Assert.Empty(_repository.Categories);
    }

    private sealed class FixedIds : IIdGenerator
    {
        public Guid Id { get; } = Guid.NewGuid();

        public Guid NewId() => Id;
    }

    private sealed class FakeCategories : IStaffCategoryRepository
    {
        public List<StaffCategory> Categories { get; } = [];

        public IReadOnlyList<StaffCategoryDto> ListResult { get; set; } = [];

        public Task<IReadOnlyList<StaffCategoryDto>> GetListAsync(
            CancellationToken ct = default) =>
            Task.FromResult(ListResult);

        public Task<StaffCategory?> GetByIdAsync(
            Guid id, CancellationToken ct = default) =>
            Task.FromResult(Categories.SingleOrDefault(x => x.Id == id));

        public Task<bool> NameExistsAsync(
            string name, Guid? exceptId = null, CancellationToken ct = default)
        {
            return Task.FromResult(
                Categories.Any(category => category.Name == name && (!exceptId.HasValue || category.Id != exceptId.Value)));
        }
        
        public void Add(StaffCategory category) => Categories.Add(category);
    }
}