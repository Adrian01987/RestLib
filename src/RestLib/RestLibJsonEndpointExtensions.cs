using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Configuration;

namespace RestLib;

/// <summary>
/// Extension methods for mapping JSON-backed RestLib resources.
/// </summary>
public static class RestLibJsonEndpointExtensions
{
    /// <summary>
    /// Maps all registered JSON-backed RestLib resources.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapJsonResources(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var registry = endpoints.ServiceProvider.GetRequiredService<RestLibJsonResourceRegistry>();
        registry.MapAll(endpoints);
        return endpoints;
    }

    /// <summary>
    /// Maps a single named JSON-backed RestLib resource.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="name">The resource name.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapJsonResource(
        this IEndpointRouteBuilder endpoints,
        string name)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var registry = endpoints.ServiceProvider.GetRequiredService<RestLibJsonResourceRegistry>();
        registry.Map(endpoints, name);
        return endpoints;
    }
}
