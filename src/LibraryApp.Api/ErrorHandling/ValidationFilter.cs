using System.Diagnostics;
using FluentValidation;
using LibraryApp.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LibraryApp.Api.ErrorHandling;

/// <summary>
/// Validates every action argument that has a FluentValidation validator (SD-15, NFR-05) before the action runs.
/// Invalid input → 400 ProblemDetails with <c>code = validation_failed</c> and <c>errors</c> per field, the same shape as
/// model-binding errors. A module only writes <c>AbstractValidator&lt;TRequest&gt;</c> in <c>Application/&lt;Module&gt;/Validators</c>;
/// validators are registered automatically.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var argument in context.ActionArguments.Values.Where(a => a is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
                continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
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

        context.Result = new BadRequestObjectResult(ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()), context.HttpContext))
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
