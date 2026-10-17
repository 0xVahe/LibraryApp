using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;

namespace LibraryApp.Api.Ui.Forms;

/// <summary>
/// The page counterpart of <c>ValidationFilter</c> (SD-15, NFR-05): before a page handler runs, every bound input that
/// has a FluentValidation validator is validated, and the errors go into <c>ModelState</c> under the field names the
/// form uses (<c>Input.Email</c> for <c>[BindProperty] Input</c>). The handler decides what to show:
/// <c>if (!ModelState.IsValid) return Page();</c> — a page usually reloads its lists before that.
/// </summary>
public sealed class UiValidationFilter(IServiceProvider services) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var ct = context.HttpContext.RequestAborted;

        // [BindProperty] properties, only when they were bound for this request (not on GET unless SupportsGet).
        foreach (var property in context.ActionDescriptor.BoundProperties)
        {
            if (property.BindingInfo?.RequestPredicate is { } bound && !bound(context))
                continue;
            if (property is PageBoundPropertyDescriptor { Property: var info })
                await ValidateAsync(services, info.GetValue(context.HandlerInstance), property.Name, context.ModelState, ct);
        }

        // Complex handler parameters (OnPostAsync(SomeRequest request)): bound without a prefix by default.
        foreach (var argument in context.HandlerArguments.Values)
            await ValidateAsync(services, argument, prefix: string.Empty, context.ModelState, ct);

        await next();
    }

    /// <summary>Validates one value with its registered validator, if any; errors are added under <paramref name="prefix"/>.</summary>
    public static async Task ValidateAsync(IServiceProvider services, object? value, string prefix, ModelStateDictionary modelState,
        CancellationToken ct)
    {
        if (value is null)
            return;
        if (services.GetService(typeof(IValidator<>).MakeGenericType(value.GetType())) is not IValidator validator)
            return;

        var result = await validator.ValidateAsync(new ValidationContext<object>(value), ct);
        foreach (var failure in result.Errors)
        {
            var key = prefix.Length == 0 ? failure.PropertyName
                : failure.PropertyName.Length == 0 ? prefix
                : $"{prefix}.{failure.PropertyName}";
            modelState.AddModelError(key, failure.ErrorMessage);
        }
    }
}
