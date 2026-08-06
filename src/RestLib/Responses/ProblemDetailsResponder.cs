using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RestLib.Configuration;

namespace RestLib.Responses;

/// <summary>
/// Binds endpoint response settings to the single Problem Details result pipeline.
/// </summary>
/// <param name="JsonOptions">The endpoint JSON serializer settings.</param>
/// <param name="Logger">The optional endpoint logger.</param>
/// <param name="Options">The RestLib response settings.</param>
internal readonly record struct ProblemDetailsResponder(
    JsonSerializerOptions? JsonOptions,
    ILogger? Logger,
    RestLibOptions? Options)
{
    /// <summary>
    /// Converts a Problem Details occurrence into an HTTP result using the bound endpoint settings.
    /// </summary>
    /// <param name="problem">The Problem Details occurrence.</param>
    /// <returns>The HTTP result.</returns>
    internal IResult Create(RestLibProblemDetails problem)
    {
        return ProblemDetailsResult.Create(problem, JsonOptions, Logger, Options);
    }
}
