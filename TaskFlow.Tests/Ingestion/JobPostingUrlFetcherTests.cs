using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using FluentAssertions;
using TaskFlow.Api.Common;
using TaskFlow.Api.Ingestion;
using TaskFlow.Tests.TestSupport;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

/// <summary>
/// Epic 3.2 Sprint 1, task S1.3, mitigation 7 (redirects capped and re-validated at every hop).
/// Uses <see cref="FakeHttpMessageHandler"/> to control exactly what <see cref="JobPostingUrlFetcher"/>
/// receives from the transport layer - no real network call, and no reliance on
/// HttpClientHandler's own auto-redirect behavior (which the fetcher must disable and replace with
/// its own manual, re-validated loop).
/// </summary>
public class JobPostingUrlFetcherTests
{
    [Fact]
    public async Task Redirect_chain_longer_than_three_hops_is_rejected()
    {
        Uri redirectTarget = new("https://example.com/job/next");
        var handler = new FakeHttpMessageHandler(request =>
        {
            HttpResponseMessage response = new(HttpStatusCode.Found);
            response.Headers.Location = redirectTarget;
            return response;
        });
        var fetcher = new JobPostingUrlFetcher(new HttpClient(handler));

        Result<string> result = await fetcher.FetchAsync(new Uri("https://example.com/job"));

        result.IsSuccess.Should().BeFalse();
        handler.CallCount.Should().Be(4);
    }

    [Fact]
    public async Task Redirect_to_a_denylisted_target_is_rejected()
    {
        Uri denylistedTarget = new("http://169.254.169.254/");
        var handler = new FakeHttpMessageHandler(request =>
        {
            HttpResponseMessage response = new(HttpStatusCode.Found);
            response.Headers.Location = denylistedTarget;
            return response;
        });
        var fetcher = new JobPostingUrlFetcher(new HttpClient(handler));

        Result<string> result = await fetcher.FetchAsync(new Uri("https://example.com/job"));

        result.IsSuccess.Should().BeFalse();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Response_larger_than_the_configured_max_size_is_rejected()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent("this body is definitely more than ten bytes long", Encoding.UTF8, "text/plain"),
            };
            return response;
        });
        var fetcher = new JobPostingUrlFetcher(new HttpClient(handler), maxResponseBytes: 10);

        Result<string> result = await fetcher.FetchAsync(new Uri("https://example.com/job"));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Response_that_never_completes_is_rejected_after_the_configured_timeout()
    {
        var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage();
        });
        var fetcher = new JobPostingUrlFetcher(new HttpClient(handler), timeout: TimeSpan.FromMilliseconds(50));

        Result<string> result = await fetcher.FetchAsync(new Uri("https://example.com/job"));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Response_with_a_disallowed_content_type_is_rejected()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/octet-stream"),
            };
            return response;
        });
        var fetcher = new JobPostingUrlFetcher(new HttpClient(handler));

        Result<string> result = await fetcher.FetchAsync(new Uri("https://example.com/job"));

        result.IsSuccess.Should().BeFalse();
    }
}
