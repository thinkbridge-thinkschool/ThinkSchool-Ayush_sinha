using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using MaintainXpert.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MaintainXpert.Api.Tests;

// Pure unit tests for the reflection-based validator, independent of the HTTP host. The Day 31
// change made this cache its per-type metadata instead of recomputing it on every call; these
// tests pin the observable behavior (which must not change) rather than the caching mechanism.
public class ValidationExtensionsTests
{
    private sealed record SampleRequest(
        [property: Required, StringLength(10, MinimumLength = 1)] string Name,
        [property: Required, StringLength(20, MinimumLength = 1)] string Description);

    private sealed record RequestWithNoValidationAttributes(string Name);

    [Fact]
    public void Validate_returns_null_for_a_value_that_satisfies_every_attribute()
    {
        var result = ValidationExtensions.Validate(new SampleRequest("Ok", "A valid description"));

        result.Should().BeNull();
    }

    [Fact]
    public void Validate_returns_a_validation_problem_for_a_blank_required_field()
    {
        var result = ValidationExtensions.Validate(new SampleRequest("", "A valid description"));

        result.Should().NotBeNull();
        ExtractErrors(result!).Should().ContainKey("Name");
    }

    [Fact]
    public void Validate_returns_a_validation_problem_when_a_string_exceeds_the_configured_length()
    {
        var result = ValidationExtensions.Validate(new SampleRequest("WayTooLongForTheLimit", "A valid description"));

        result.Should().NotBeNull();
        ExtractErrors(result!).Should().ContainKey("Name");
    }

    [Fact]
    public void Validate_reports_every_invalid_property_not_just_the_first()
    {
        var result = ValidationExtensions.Validate(new SampleRequest("", ""));

        ExtractErrors(result!).Should().ContainKeys("Name", "Description");
    }

    [Fact]
    public void Validate_returns_null_for_a_type_with_no_validation_attributes()
    {
        var result = ValidationExtensions.Validate(new RequestWithNoValidationAttributes(""));

        result.Should().BeNull();
    }

    [Fact]
    public void Validate_is_independent_across_successive_calls_for_the_same_cached_type()
    {
        // Exercises the per-type cache twice in a row with different instances, to confirm
        // the cached metadata carries no per-call state that could leak between requests.
        var first = ValidationExtensions.Validate(new SampleRequest("", "A valid description"));
        var second = ValidationExtensions.Validate(new SampleRequest("Ok", "A valid description"));
        var third = ValidationExtensions.Validate(new SampleRequest("", "A valid description"));

        first.Should().NotBeNull();
        second.Should().BeNull();
        third.Should().NotBeNull();
    }

    private static IDictionary<string, string[]> ExtractErrors(IResult result)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        var validationDetails = Assert.IsType<HttpValidationProblemDetails>(problem.ProblemDetails);
        return validationDetails.Errors;
    }
}
