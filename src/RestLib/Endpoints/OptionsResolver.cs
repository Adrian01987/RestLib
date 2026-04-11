using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestLib.Configuration;
using RestLib.Logging;
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

        var registeredOptions = httpContext.RequestServices.GetService<RestLibOptions>();
        if (registeredOptions is null)
        {
            var logger = httpContext.RequestServices.GetService<ILoggerFactory>()
                ?.CreateLogger("RestLib.OptionsResolver");
            if (logger is not null)
            {
                RestLibLogMessages.OptionsNotRegistered(logger);
            }
        }

        var options = registeredOptions ?? new RestLibOptions();
        return (jsonOptions, options);
    }
}
