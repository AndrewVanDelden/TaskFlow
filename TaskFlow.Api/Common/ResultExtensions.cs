using Microsoft.AspNetCore.Mvc;

namespace TaskFlow.Api.Common;

public static class ResultExtensions
{
    /// <summary>Maps a service Result onto the conventional HTTP status codes.</summary>
    public static IActionResult ToActionResult<T>(this Result<T> result) => result.Status switch
    {
        ResultStatus.Ok           => new OkObjectResult(result.Value),
        ResultStatus.NotFound     => new NotFoundObjectResult(new { message = result.Error }),
        ResultStatus.Conflict     => new ConflictObjectResult(new { message = result.Error }),
        ResultStatus.Validation   => new BadRequestObjectResult(new { message = result.Error }),
        ResultStatus.Unauthorized => new UnauthorizedObjectResult(new { message = result.Error }),
        _                         => new StatusCodeResult(500)
    };
}