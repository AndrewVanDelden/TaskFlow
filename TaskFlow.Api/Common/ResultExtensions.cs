using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Export;

namespace TaskFlow.Api.Common;

public static class ResultExtensions
{
    /// <summary>Maps a service Result onto the conventional HTTP status codes.</summary>
    public static IActionResult ToActionResult<T>(this Result<T> result) => result.Status switch
    {
        ResultStatus.Ok           => OkJson(result.Value),
        ResultStatus.NotFound     => new NotFoundObjectResult(new { message = result.Error }),
        ResultStatus.Conflict     => new ConflictObjectResult(new { message = result.Error }),
        ResultStatus.Validation   => new BadRequestObjectResult(new { message = result.Error }),
        ResultStatus.Unauthorized => new UnauthorizedObjectResult(new { message = result.Error }),
        _                         => new StatusCodeResult(500)
    };

    // Deliberate, minor exception to "one switch per shape": a JSON-wrapping ToActionResult is
    // wrong for a PDF/Markdown byte payload (T5.2), so this is its own small extension for the
    // file-download response shape rather than folding a callback/strategy abstraction into the
    // generic switch above, which this codebase's style doesn't use elsewhere.
    /// <summary>Maps a service Result&lt;ExportedFile&gt; onto a file download response. Success
    /// becomes a FileContentResult whose FileDownloadName sets the Content-Disposition: attachment
    /// header, which is what makes the browser download rather than display the response.</summary>
    public static IActionResult ToFileActionResult(this Result<ExportedFile> result) => result.Status switch
    {
        ResultStatus.Ok           => new FileContentResult(result.Value!.Content, result.Value.ContentType) { FileDownloadName = result.Value.FileName },
        ResultStatus.NotFound     => new NotFoundObjectResult(new { message = result.Error }),
        ResultStatus.Conflict     => new ConflictObjectResult(new { message = result.Error }),
        ResultStatus.Validation   => new BadRequestObjectResult(new { message = result.Error }),
        ResultStatus.Unauthorized => new UnauthorizedObjectResult(new { message = result.Error }),
        _                         => new StatusCodeResult(500)
    };

    // Sprint 4R: GetResumeContext returns Result<string> for a "bare JSON string" HTTP contract.
    // Without pinning the content type, ASP.NET Core's default StringOutputFormatter intercepts any
    // ObjectResult whose value is exactly typeof(string) and writes it as unquoted text/plain
    // instead of a JSON string - breaking that contract. Restricting ContentTypes to
    // application/json removes StringOutputFormatter from consideration; harmless for every other
    // T, which was already being serialized as JSON anyway.
    private static OkObjectResult OkJson<T>(T? value)
    {
        var result = new OkObjectResult(value);
        result.ContentTypes.Add("application/json");
        return result;
    }
}