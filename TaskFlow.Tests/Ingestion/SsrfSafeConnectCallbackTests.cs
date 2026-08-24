using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using TaskFlow.Api.Ingestion;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

/// <summary>
/// Epic 3.2 Sprint 1, mitigation 6 (DNS-rebinding-safe connection): proves
/// <see cref="SsrfSafeConnectCallback"/> performs its own DNS resolution and rejects the connection
/// when every resolved address is denylisted - not a re-test of <see cref="UrlValidation"/>'s
/// string-based hostname check under a different name. Both cases use an innocent-looking hostname
/// that would pass <see cref="UrlValidation"/>'s hostname denylist, so a passing test here can only
/// be explained by the connect-time IP check actually running.
///
/// Tests call the <see cref="DnsEndPoint"/>-based overload directly, not the
/// <c>SocketsHttpConnectionContext</c> one: that type has no public constructor (confirmed against
/// the .NET 10 API docs - it exists only to be handed to a <c>ConnectCallback</c> by the runtime
/// itself), so it cannot be built in test code. The context-based overload this class also exposes
/// is a one-line delegation to this one, for wiring into <c>SocketsHttpHandler.ConnectCallback</c>;
/// it has no logic of its own to test.
/// </summary>
public class SsrfSafeConnectCallbackTests
{
    [Fact]
    public async Task Connect_is_rejected_when_every_resolved_address_is_denylisted()
    {
        const string innocentLookingHostname = "internal-payroll-app.example.com";
        var resolver = new Mock<IDnsResolver>();
        resolver.Setup(r => r.ResolveAsync(innocentLookingHostname, It.IsAny<CancellationToken>()))
            .ReturnsAsync([IPAddress.Parse("169.254.169.254")]);
        var callback = new SsrfSafeConnectCallback(resolver.Object);
        DnsEndPoint endpoint = new(innocentLookingHostname, 443);

        Func<Task> act = () => callback.ConnectAsync(endpoint, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<HttpRequestException>();
        resolver.Verify(r => r.ResolveAsync(innocentLookingHostname, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Connect_is_rejected_when_resolver_returns_no_addresses()
    {
        const string innocentLookingHostname = "example.com";
        var resolver = new Mock<IDnsResolver>();
        resolver.Setup(r => r.ResolveAsync(innocentLookingHostname, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var callback = new SsrfSafeConnectCallback(resolver.Object);
        DnsEndPoint endpoint = new(innocentLookingHostname, 443);

        Func<Task> act = () => callback.ConnectAsync(endpoint, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<HttpRequestException>();
        resolver.Verify(r => r.ResolveAsync(innocentLookingHostname, It.IsAny<CancellationToken>()), Times.Once);
    }

    // PR #63 review finding: both defense layers share UrlValidation.IsDenylistedIpAddress, so the
    // IPv4-mapped IPv6 bypass affects this connect-time check exactly as it affects the pre-check -
    // a resolver returning an IPv4-mapped cloud-metadata address must still be rejected here.
    [Fact]
    public async Task Connect_is_rejected_when_resolved_address_is_an_ipv4_mapped_ipv6_cloud_metadata_address()
    {
        const string innocentLookingHostname = "internal-payroll-app.example.com";
        var resolver = new Mock<IDnsResolver>();
        resolver.Setup(r => r.ResolveAsync(innocentLookingHostname, It.IsAny<CancellationToken>()))
            .ReturnsAsync([IPAddress.Parse("::ffff:169.254.169.254")]);
        var callback = new SsrfSafeConnectCallback(resolver.Object);
        DnsEndPoint endpoint = new(innocentLookingHostname, 443);

        Func<Task> act = () => callback.ConnectAsync(endpoint, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
