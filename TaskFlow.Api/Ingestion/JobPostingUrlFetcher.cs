using System.Net;
using System.Net.Http;
using System.Text;
using TaskFlow.Api.Common;

namespace TaskFlow.Api.Ingestion;

/// <summary>
/// Fetches a job posting URL over HTTP with a manual, re-validated redirect loop (Epic 3.2
/// security design, mitigation 7): every redirect target is checked with
/// <see cref="UrlValidation.Validate"/> before it is ever requested, and the chain is capped at
/// <see cref="MaxRedirects"/> hops, so a redirect cannot be used to reach a target the initial
/// URL check would have rejected. Also enforces a total-fetch timeout (mitigation 9), a bounded
/// streaming read capped at a maximum response size (mitigation 8), and a Content-Type allowlist
/// on the terminal response (mitigation 10).
/// </summary>
public sealed class JobPostingUrlFetcher : IJobPostingUrlFetcher
{
    private const int MaxRedirects = 3;

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;
    private readonly long _maxResponseBytes;

    public JobPostingUrlFetcher(HttpClient httpClient, TimeSpan? timeout = null, long? maxResponseBytes = null)
    {
        _httpClient = httpClient;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _maxResponseBytes = maxResponseBytes ?? 5L * 1024 * 1024;
    }

    public async Task<Result<string>> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        Result<Uri> validation = UrlValidation.Validate(uri);
        if (!validation.IsSuccess)
            return Result<string>.Invalid(validation.Error!);

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            Uri currentUri = uri;
            int redirectCount = 0;

            while (true)
            {
                // PR #63 review finding: the default HttpCompletionOption.ResponseContentRead
                // buffers the entire response body in memory before GetAsync even returns, which
                // defeats the size cap below - a malicious server could OOM the process before
                // ReadBoundedAsync ever gets a chance to enforce _maxResponseBytes.
                // ResponseHeadersRead returns as soon as headers arrive, so the body is streamed
                // lazily and the bounded read actually gates what gets buffered.
                using HttpResponseMessage response =
                    await _httpClient.GetAsync(currentUri, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

                if (!IsRedirectStatusCode(response.StatusCode))
                {
                    string? mediaType = response.Content.Headers.ContentType?.MediaType;
                    if (!IsAllowedContentType(mediaType))
                        return Result<string>.Invalid($"Content-Type '{mediaType ?? "(none)"}' is not allowed; only text/html and text/plain are permitted.");

                    Result<string> rawBody = await ReadBoundedAsync(response.Content, _maxResponseBytes, timeoutCts.Token);
                    return rawBody.IsSuccess ? Result<string>.Ok(HtmlTextExtractor.ExtractText(rawBody.Value!)) : rawBody;
                }

                if (redirectCount >= MaxRedirects)
                    return Result<string>.Invalid($"URL redirected more than {MaxRedirects} times.");

                Uri? location = response.Headers.Location;
                if (location is null)
                    return Result<string>.Invalid("Redirect response did not include a Location header.");

                Uri redirectTarget = location.IsAbsoluteUri ? location : new Uri(currentUri, location);

                Result<Uri> redirectValidation = UrlValidation.Validate(redirectTarget);
                if (!redirectValidation.IsSuccess)
                    return Result<string>.Invalid(redirectValidation.Error!);

                currentUri = redirectTarget;
                redirectCount++;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<string>.Invalid($"Request timed out after {_timeout.TotalSeconds} seconds.");
        }
        // PR #63 review finding: DNS resolution failure, connection refused, or (in production)
        // SsrfSafeConnectCallback rejecting a DNS-rebinding attempt at connect time all surface as
        // HttpRequestException from GetAsync. Without this catch it propagated unhandled instead of
        // becoming a Result<string>.Invalid like every other rejection path in this fetcher.
        catch (HttpRequestException ex)
        {
            return Result<string>.Invalid($"Failed to fetch the URL: {ex.Message}");
        }
    }

    private static async Task<Result<string>> ReadBoundedAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[8192];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + bytesRead > maxBytes)
                return Result<string>.Invalid($"Response exceeded the maximum allowed size of {maxBytes} bytes.");

            await buffer.WriteAsync(chunk.AsMemory(0, bytesRead), cancellationToken);
        }

        return Result<string>.Ok(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static bool IsAllowedContentType(string? mediaType) =>
        mediaType is not null &&
        (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(mediaType, "text/plain", StringComparison.OrdinalIgnoreCase));

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect => true,
        _ => false,
    };
}
