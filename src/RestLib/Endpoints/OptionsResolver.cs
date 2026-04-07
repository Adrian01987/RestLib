using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Configuration;
using RestLib.Serialization;

namespace RestLib.Endpoints;

/// <summary>
/// Helper methods for resolving RestLib options and JSON serializer options from DI.
/// </summary>
internal static class OptionsResolver
{
    /// <summary>
    /// Resolves the JSON serializer options and RestLib options from the request services.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>A tuple containing the JSON serializer options and RestLib options.</returns>
    internal static (JsonSerializerOptions JsonOptions, RestLibOptions Options) ResolveOptions(
        HttpContext httpContext)
    {
        var jsonOptions = httpContext.RequestServices.GetService<JsonSerializerOptions>()
                          ?? RestLibJsonOptions.CreateDefault();
        var options = httpContext.RequestServices.GetService<RestLibOptions>()
                      ?? new RestLibOptions();
        return (jsonOptions, options);
    }
}
