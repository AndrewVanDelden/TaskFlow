namespace TaskFlow.Api.Export;

/// <summary>What IExportService hands back on success — everything a controller needs to turn
/// into a file download response (ResultExtensions.ToFileActionResult).</summary>
public record ExportedFile(byte[] Content, string ContentType, string FileName);
