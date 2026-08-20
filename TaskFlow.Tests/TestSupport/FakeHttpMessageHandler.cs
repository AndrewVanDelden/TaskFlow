using System.Net.Http;

namespace TaskFlow.Tests.TestSupport;

/// <summary>
/// Fakes the HTTP transport layer under an HttpClient so tests can control exact responses
/// (including redirects) without a real network call, and without HttpClientHandler's own
/// auto-redirect-following interfering (a raw HttpMessageHandler never auto-follows redirects -
/// that behavior belongs to HttpClientHandler/SocketsHttpHandler, not HttpClient itself - so
/// responses returned here are exactly what the code under test receives).
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respondAsync;
    public int CallCount { get; private set; }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this((request, _) => Task.FromResult(respond(request)))
    {
    }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respondAsync) =>
        _respondAsync = respondAsync;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return await _respondAsync(request, cancellationToken);
    }
}
