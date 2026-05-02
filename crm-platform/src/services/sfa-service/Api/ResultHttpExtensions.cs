using CrmPlatform.ServiceTemplate.Domain;
using Microsoft.AspNetCore.Http;

namespace CrmPlatform.SfaService.Api;

internal static class ResultHttpExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.Error is null
            ? Results.BadRequest()
            : ToHttpResult(result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result) =>
        result.Error is null
            ? Results.BadRequest()
            : ToHttpResult(result.Error);

    private static IResult ToHttpResult(ResultError error) => error.Code switch
    {
        ResultErrorCode.NotFound => Results.NotFound(new { error = error.Message }),
        ResultErrorCode.Forbidden or ResultErrorCode.TenantMismatch => Results.Forbid(),
        ResultErrorCode.Conflict => Results.Conflict(new { error = error.Message }),
        ResultErrorCode.ValidationError => Results.UnprocessableEntity(new { error = error.Message }),
        ResultErrorCode.ExternalServiceError => Results.Problem(
            detail: error.Message,
            statusCode: StatusCodes.Status502BadGateway),
        _ => Results.BadRequest(new { error = error.Message })
    };
}
