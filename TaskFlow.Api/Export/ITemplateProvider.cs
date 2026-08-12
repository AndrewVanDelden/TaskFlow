using TaskFlow.Api.Common;

namespace TaskFlow.Api.Export;

/// <summary>
/// Reads a Typst template file by name. A failure (missing file, permission denied) is reported as
/// a failed <see cref="Result{T}"/>, never a thrown exception, matching this codebase's service
/// convention.
/// </summary>
public interface ITemplateProvider
{
    Result<string> GetTemplateText(string fileName);
}
