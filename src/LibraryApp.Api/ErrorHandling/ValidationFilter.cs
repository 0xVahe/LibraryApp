using System.Diagnostics;
using FluentValidation;
using LibraryApp.Application.Common;
using LibraryApp.Application.Staff.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LibraryApp.Api.ErrorHandling;

/// <summary>
/// Validates every action argument that has a FluentValidation validator (SD-15, NFR-05) before the action runs.
/// Invalid input → 400 ProblemDetails with <c>errors</c> per field. Staff category requests expose the first
/// staff-specific validation code; other requests and model-binding errors use <c>code = validation_failed</c>.
/// A module only writes <c>AbstractValidator&lt;TRequest&gt;</c> in <c>Application/&lt;Module&gt;/Validators</c>;
/// validators are registered automatically.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        string? categoryErrorCode = null;
        
        foreach (var argument in context.ActionArguments.Values.Where(a => a is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
                continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                if (argument is UpsertStaffCategoryRequest &&
                    failure.ErrorCode.StartsWith("staff.", StringComparison.Ordinal))
                {
                    categoryErrorCode ??= failure.ErrorCode;
                }
                
                if (!errors.TryGetValue(failure.PropertyName, out var messages))
                    errors[failure.PropertyName] = messages = [];
                messages.Add(failure.ErrorMessage);
            }
        }

        if (errors.Count == 0)
        {
            await next();
            return;
        }

        var problem = ValidationProblem(
            errors.ToDictionary(e => e.Key, e => e.Value.ToArray()),
            context.HttpContext);

        if (categoryErrorCode is not null)
        {
            problem.Extensions["code"] = categoryErrorCode;
            problem.Type = $"urn:library:error:{categoryErrorCode}";
        }

        context.Result = new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" },
        };
    }

    public static ValidationProblemDetails ValidationProblem(IDictionary<string, string[]> errors, HttpContext httpContext)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Detail = CommonErrors.ValidationFailed.Message,
            Type = $"urn:library:error:{CommonErrors.ValidationFailed.Code}",
        };
        problem.Extensions["code"] = CommonErrors.ValidationFailed.Code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        return problem;
    }
}
