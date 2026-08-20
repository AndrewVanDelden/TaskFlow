using FluentAssertions;
using TaskFlow.Api.Common;
using TaskFlow.Api.Ingestion;
using Xunit;

namespace TaskFlow.Tests.Ingestion;

public class UrlValidationTests
{
    [Fact]
    public void Https_url_with_default_port_and_no_credentials_is_accepted()
    {
        Uri uri = new("https://example.com/job");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Http_url_with_default_port_and_no_credentials_is_accepted()
    {
        Uri uri = new("http://example.com/job");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Ftp_scheme_is_rejected()
    {
        Uri uri = new("ftp://example.com");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void File_scheme_is_rejected()
    {
        Uri uri = new("file:///etc/passwd");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Url_with_embedded_credentials_is_rejected()
    {
        Uri uri = new("http://user:pass@example.com");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("credentials");
    }

    [Fact]
    public void Url_with_non_default_port_is_rejected()
    {
        Uri uri = new("http://example.com:8080");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("port");
    }

    [Fact]
    public void Localhost_hostname_is_rejected()
    {
        Uri uri = new("http://localhost");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Cloud_metadata_link_local_ip_is_rejected()
    {
        Uri uri = new("http://169.254.169.254");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Loopback_ip_address_is_rejected()
    {
        Uri uri = new("http://127.0.0.1");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Private_rfc1918_10_range_ip_is_rejected()
    {
        Uri uri = new("http://10.0.0.5");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Private_rfc1918_192_168_range_ip_is_rejected()
    {
        Uri uri = new("http://192.168.1.1");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Private_rfc1918_172_16_range_ip_is_rejected()
    {
        Uri uri = new("http://172.20.0.1");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Loopback_ipv6_address_is_rejected()
    {
        Uri uri = new("http://[::1]");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Hostname_with_dot_local_suffix_is_rejected()
    {
        Uri uri = new("http://internal-tool.local");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Single_label_hostname_with_no_dot_is_rejected()
    {
        Uri uri = new("http://intranet");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Unspecified_ipv4_address_is_rejected()
    {
        Uri uri = new("http://0.0.0.0");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Unspecified_ipv6_address_is_rejected()
    {
        Uri uri = new("http://[::]");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Link_local_ipv6_address_is_rejected()
    {
        Uri uri = new("http://[fe80::1]");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Unique_local_ipv6_address_is_rejected()
    {
        Uri uri = new("http://[fc00::1]");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Multicast_ipv4_address_is_rejected()
    {
        Uri uri = new("http://224.0.0.1");

        Result<Uri> result = UrlValidation.Validate(uri);

        result.IsSuccess.Should().BeFalse();
    }
}
