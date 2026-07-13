using ErrorOr;
using RentifyxCommunications.Domain.Constants;

namespace RentifyxCommunications.Api.Extensions;

internal static class ErrorOrExtensions
{
    public static IResult ToProblem(this List<Error> errors, HttpContext httpContext)
    {
        Error firstError = errors[0];

        int statusCode = firstError.Type switch
        {
            ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };

        string? correlationId = httpContext.Items[CorrelationIdConstants.Key]?.ToString();

        if (firstError.Type is ErrorType.Validation)
        {
            Dictionary<string, string[]> validationErrors = errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

            return Results.ValidationProblem(
                validationErrors,
                statusCode: statusCode,
                extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId });
        }

        return Results.Problem(
            title: firstError.Description,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId });
    }

    public static IResult ToResult<T>(this ErrorOr<T> result, HttpContext httpContext)
        => result.Match(
            value => Results.Ok(value),
            errors => errors.ToProblem(httpContext));

    public static IResult ToCreatedResult<T>(this ErrorOr<T> result, string uri, HttpContext httpContext)
        => result.Match(
            value => Results.Created(uri, value),
            errors => errors.ToProblem(httpContext));
}
