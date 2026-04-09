using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RestLib.Responses;

namespace RestLib.Tests.Fakes;

/// <summary>
/// Shared assertion helpers for verifying RFC 9457 Problem Details responses.
/// Reduces boilerplate across integration tests that check error responses.
/// </summary>
internal static class ProblemDetailsAssert
{
    /// <summary>
    /// Asserts that the response is a Problem Details response with the expected
    /// status code and problem type, then returns the deserialized
    /// <see cref="RestLibProblemDetails"/> for further assertions.
    /// </summary>
    /// <param name="response">The HTTP response to verify.</param>
    /// <param name="expectedStatusCode">The expected HTTP status code.</param>
    /// <param name="expectedType">The expected problem type URI (e.g. "/problems/not-found").</param>
    /// <param name="expectedTitle">Optional expected title (e.g. "Resource Not Found").</param>
    /// <param name="expectedDetail">Optional expected detail substring to assert with <c>Contains</c>.</param>
    /// <returns>The deserialized <see cref="RestLibProblemDetails"/> for additional assertions.</returns>
    public static async Task<RestLibProblemDetails> ShouldBeProblemDetails(
        this HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string expectedType,
        string? expectedTitle = null,
        string? expectedDetail = null)
    {
        response.StatusCode.Should().Be(expectedStatusCode);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<RestLibProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(expectedType);
        problem.Status.Should().Be((int)expectedStatusCode);

        if (expectedTitle is not null)
        {
            problem.Title.Should().Be(expectedTitle);
        }

        if (expectedDetail is not null)
        {
            problem.Detail.Should().Contain(expectedDetail);
        }

        return problem;
    }

    /// <summary>
    /// Asserts that the response is a Problem Details response with the expected
    /// status code and problem type, then returns the raw <see cref="JsonElement"/>
    /// for further property-level assertions (e.g. inspecting the <c>errors</c> dictionary).
    /// </summary>
    /// <param name="response">The HTTP response to verify.</param>
    /// <param name="expectedStatusCode">The expected HTTP status code.</param>
    /// <param name="expectedType">The expected problem type URI (e.g. "/problems/invalid-fields").</param>
    /// <param name="expectedTitle">Optional expected title to assert.</param>
    /// <returns>The raw <see cref="JsonElement"/> for additional property-level assertions.</returns>
    public static async Task<JsonElement> ShouldBeProblemDetailsJson(
        this HttpResponseMessage response,
        HttpStatusCode expectedStatusCode,
        string expectedType,
        string? expectedTitle = null)
    {
        response.StatusCode.Should().Be(expectedStatusCode);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("type").GetString().Should().Be(expectedType);
        json.GetProperty("status").GetInt32().Should().Be((int)expectedStatusCode);

        if (expectedTitle is not null)
        {
            json.GetProperty("title").GetString().Should().Be(expectedTitle);
        }

        return json;
    }
}
