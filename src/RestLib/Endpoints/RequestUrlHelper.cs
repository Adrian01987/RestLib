using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace RestLib.Endpoints;

/// <summary>
/// Builds application-aware URLs from the normalized ASP.NET Core request.
/// </summary>
internal static class RequestUrlHelper
{
    /// <summary>
    /// Builds an absolute URL for the current request path, including <see cref="HttpRequest.PathBase"/>.
    /// </summary>
    /// <param name="request">The current HTTP request.</param>
    /// <returns>The absolute URL without the query string.</returns>
    internal static string BuildAbsoluteCurrentPath(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return UriHelper.BuildAbsolute(
            request.Scheme,
            request.Host,
            request.PathBase,
            request.Path);
    }

    /// <summary>
    /// Builds an absolute URL from an already encoded application-relative path.
    /// </summary>
    /// <param name="request">The current HTTP request.</param>
    /// <param name="encodedPath">The encoded path, beginning with '/'.</param>
    /// <returns>The absolute URL including <see cref="HttpRequest.PathBase"/>.</returns>
    internal static string BuildAbsolute(HttpRequest request, string encodedPath)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(encodedPath);

        var applicationBaseUrl = UriHelper.BuildAbsolute(
            request.Scheme,
            request.Host,
            request.PathBase);
        return $"{applicationBaseUrl.TrimEnd('/')}{encodedPath}";
    }

    /// <summary>
    /// Builds a root-relative URL for a resource below the current request path.
    /// </summary>
    /// <param name="request">The current HTTP request.</param>
    /// <param name="encodedSuffix">The encoded resource path suffix, beginning with '/'.</param>
    /// <returns>The root-relative URL including <see cref="HttpRequest.PathBase"/>.</returns>
    internal static string BuildRelativeResourcePath(HttpRequest request, string encodedSuffix)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(encodedSuffix);

        return $"{UriHelper.BuildRelative(request.PathBase, request.Path)}{encodedSuffix}";
    }
}
