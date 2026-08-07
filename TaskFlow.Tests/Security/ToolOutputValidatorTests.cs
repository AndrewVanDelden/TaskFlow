using FluentAssertions;
using TaskFlow.Api.Common;
using TaskFlow.Api.Security;
using Xunit;

namespace TaskFlow.Tests.Security;

public class ToolOutputValidatorTests
{
    [Fact]
    public void Rejects_content_longer_than_max_length()
    {
        var oversized = new string('a', 11);

        var result = ToolOutputValidator.Validate(oversized, maxLength: 10);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Validation);
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Rejects_null_content()
    {
        var result = ToolOutputValidator.Validate(null, maxLength: 100);

        result.IsSuccess.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Validation);
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Rejects_empty_or_whitespace_only_content()
    {
        ToolOutputValidator.Validate("", maxLength: 100).Status.Should().Be(ResultStatus.Validation);
        ToolOutputValidator.Validate("   ", maxLength: 100).Status.Should().Be(ResultStatus.Validation);
    }

    [Fact]
    public void Accepts_valid_content_within_bounds_unchanged()
    {
        const string content = "A tailored resume body.";

        var result = ToolOutputValidator.Validate(content, maxLength: 100);

        result.IsSuccess.Should().BeTrue();
        result.Status.Should().Be(ResultStatus.Ok);
        result.Value.Should().Be(content);
    }
}
