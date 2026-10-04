using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Bagatka.Foundation.Web;

/// <summary>
/// Maps results to HTTP in one place (PATTERNS.md, entry 11): values to their success status, and
/// errors to problem details carrying the error's code.
/// </summary>
public static class ResultHttpExtensions
{
    /// <summary>200 with the value, or the error's problem.</summary>
    public static Results<Ok<T>, ProblemHttpResult> ToOk<T>(this Result<T> result)
        where T : notnull
    {
        if (result.Failed)
        {
            return result.Error.ToProblem();
        }

        return TypedResults.Ok(result.Output);
    }

    /// <summary>201 with the value and the location of its own route, or the error's problem.</summary>
    public static Results<Created<T>, ProblemHttpResult> ToCreated<T>(this Result<T> result, Func<T, string> location)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(location);
        if (result.Failed)
        {
            return result.Error.ToProblem();
        }

        Uri created = new Uri(location(result.Output), UriKind.Relative);
        return TypedResults.Created(created, result.Output);
    }

    /// <summary>204, or the error's problem.</summary>
    public static Results<NoContent, ProblemHttpResult> ToNoContent(this Result result)
    {
        if (result.Failed)
        {
            return result.Error.ToProblem();
        }

        return TypedResults.NoContent();
    }

    /// <summary>
    /// The error as problem details: the status its kind maps to, its message as the title, its code
    /// as <c>code</c>, and for validation, the rejected fields as <c>errors</c>.
    /// </summary>
    public static ProblemHttpResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ProblemDetails problem = error.Kind == ErrorKind.Validation
            ? new HttpValidationProblemDetails(error.Fields
                .GroupBy(field => field.Field, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(field => field.Message).ToArray(), StringComparer.Ordinal))
            : new ProblemDetails();
        problem.Status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
        };
        problem.Title = error.Message;
        problem.Extensions["code"] = error.Code;
        return TypedResults.Problem(problem);
    }
}
