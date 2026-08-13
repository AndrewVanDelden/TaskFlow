using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Api.Common;

namespace TaskFlow.Tests.Common;

/// <summary>
/// Sprint 5 T5.1a: covers the new <see cref="ResultStatus.Error"/> status/factory added for
/// internal failures (e.g. the Typst compiler subprocess failing or timing out) that don't fit
/// any existing status. See "Decisions owned here" under Sprint 5 in
/// TaskFlow_Epic3_ResumeBuilder.md.
/// </summary>
public class ResultTests
{
    [Fact]
    public void InternalError_factory_sets_Error_status_and_is_not_success()
    {
        var result = Result<string>.InternalError("boom");

        result.Status.Should().Be(ResultStatus.Error);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("boom");
        result.Value.Should().BeNull();
    }

    [Fact]
    public void ToActionResult_maps_Error_status_to_500()
    {
        var result = Result<string>.InternalError("boom");

        var actionResult = result.ToActionResult();

        actionResult.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(500);
    }
}
