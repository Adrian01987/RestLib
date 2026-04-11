using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RestLib.Logging;

/// <summary>
/// Resolves an <see cref="ILogger"/> from the request services at request-time.
/// Mirrors the <c>OptionsResolver</c> pattern: resolve from DI, fall back to a safe default.
/// </summary>
internal static class RestLibLoggerResolver
{
    /// <summary>
    /// Resolves an <see cref="ILogger"/> with the given category name from the current request's service provider.
    /// Returns <see cref="NullLogger.Instance"/> if <see cref="ILoggerFactory"/> is not registered.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="categoryName">The logger category name (e.g., <c>"RestLib.GetAll"</c>).</param>
    /// <returns>An <see cref="ILogger"/> instance for the specified category.</returns>
    internal static ILogger ResolveLogger(HttpContext httpContext, string categoryName)
    {
        var factory = httpContext.RequestServices.GetService<ILoggerFactory>();
        if (factory is null)
        {
            return NullLogger.Instance;
        }

        return factory.CreateLogger(categoryName);
    }
}
