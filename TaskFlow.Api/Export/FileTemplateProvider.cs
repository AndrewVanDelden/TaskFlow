using System.Collections.Concurrent;
using TaskFlow.Api.Common;

namespace TaskFlow.Api.Export;

/// <summary>
/// Reads a Typst template file and caches successful reads for this instance's lifetime
/// (registered Singleton in Program.cs, so effectively process-wide - the template files are
/// trusted, hand-authored, and static). Only successful reads are cached: a transient failure
/// (permission glitch, disk hiccup) must never permanently break every later export of that
/// document kind, so a failure is never cached and is retried on the next call.
/// </summary>
public class FileTemplateProvider : ITemplateProvider
{
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public Result<string> GetTemplateText(string fileName)
    {
        if (_cache.TryGetValue(fileName, out var cached))
            return Result<string>.Ok(cached);

        try
        {
            var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Export", "Templates", fileName));
            _cache.TryAdd(fileName, text);
            return Result<string>.Ok(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<string>.InternalError($"Failed to load export template '{fileName}'.");
        }
    }
}
