using LibraryApp.Application.Abstractions.Common;



using LibraryApp.Application.Staff.Dto;



using LibraryApp.Application.Staff.Ports;



using LibraryApp.Domain.Common;



using LibraryApp.Domain.Staff;



using LibraryApp.Application.Common;







namespace LibraryApp.Application.Staff.Services;







public sealed class StaffCategoryService(



    IStaffCategoryRepository repository,



    IUnitOfWork unitOfWork,



    IIdGenerator ids) : IStaffCategoryService



{



    public Task<IReadOnlyList<StaffCategoryDto>> GetListAsync(CancellationToken ct = default)



    {



        return repository.GetListAsync(ct);



    }







    public async Task<StaffCategoryDto> GetAsync(Guid id, CancellationToken ct = default)



    {



        var category = await FindAsync(id, ct);



        return ToDto(category);



    }







    public Task<StaffCategoryDto> CreateAsync(UpsertStaffCategoryRequest request, CancellationToken ct = default)



    {



        return SaveAsync(async token =>



        {



            var category = new StaffCategory(



                ids.NewId(),



                request.Name,



                request.MaxLoans,



                request.LoanDays,



                request.MaxRenewals);







            await RequireUniqueNameAsync(category.Name, null, token);







            repository.Add(category);







            return ToDto(category);



        }, ct);



    }







    /// <summary>



    /// Changes category settings for future loans.



    /// Existing loan records are not modified.



    /// </summary>



    public Task<StaffCategoryDto> UpdateAsync(



        Guid id,



        UpsertStaffCategoryRequest request,



        CancellationToken ct = default)



    {



        return SaveAsync(async token =>



        {



            var category = await FindAsync(id, token);







            var name = request.Name?.Trim() ?? string.Empty;



            await RequireUniqueNameAsync(name, id, token);







            category.Rename(name);



            category.SetLimits(



                request.MaxLoans,



                request.LoanDays,



                request.MaxRenewals);







            return ToDto(category);



        }, ct);



    }







    private async Task<StaffCategory> FindAsync(Guid id, CancellationToken ct)



    {



        return await repository.GetByIdAsync(id, ct)



            ?? throw new DomainException(StaffErrors.CategoryResourceNotFound);



    }







    private static StaffCategoryDto ToDto(StaffCategory category)



    {



        return new StaffCategoryDto(



            category.Id,



            category.Name,



            category.MaxLoans,



            category.LoanDays,



            category.MaxRenewals);



    }







    private async Task RequireUniqueNameAsync(string name, Guid? exceptId, CancellationToken ct)



    {



        if (await repository.NameExistsAsync(name, exceptId, ct))



            throw new DomainException(StaffErrors.CategoryNameTaken);



    }







    private async Task<StaffCategoryDto> SaveAsync(



        Func<CancellationToken, Task<StaffCategoryDto>> operation,



        CancellationToken ct)



    {



        try {



            return await unitOfWork.ExecuteInTransactionAsync(operation, ct);



        }



        catch (DomainException ex) when (ex.Error.Code == CommonErrors.UniqueViolation.Code) {



            throw new DomainException(StaffErrors.CategoryNameTaken, ex);



        }



    }



}
