using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ShopFlow.ServiceDefaults;

/// <summary>Turns FluentValidation failures into RFC 7807 400 responses.</summary>
public class ValidationExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not ValidationException ve) return false;
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        var errors = ve.Errors.GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ValidationProblemDetails(errors) { Status = 400, Title = "Validation failed" },
            Exception = exception,
        });
    }
}
