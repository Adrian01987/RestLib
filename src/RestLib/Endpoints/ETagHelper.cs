using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Configuration;

namespace RestLib.Endpoints;

/// <summary>
/// Helper methods for ETag generation and If-Match precondition checking.
/// </summary>
internal static class ETagHelper
{
    /// <summary>
    /// Resolves the ETag generator from the service provider.
    /// This method is only called when <see cref="RestLibOptions.EnableETagSupport"/> is <c>true</c>,
    /// which guarantees an <see cref="IETagGenerator"/> singleton was registered by
    /// <see cref="RestLibServiceExtensions.AddRestLib"/>.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>The resolved ETag generator.</returns>
    internal static IETagGenerator ResolveETagGenerator(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<IETagGenerator>();

    /// <summary>
    /// Checks the If-Match precondition header when ETag support is enabled.
    /// If the header is present, fetches the current entity, compares ETags,
    /// and returns the appropriate error result if the precondition fails.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="repository">The repository to fetch the entity from.</param>
    /// <param name="id">The entity identifier.</param>
    /// <param name="entityName">The clean entity type name used in error messages.</param>
    /// <param name="options">The RestLib options (checked for ETag support).</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// A tuple where <c>Entity</c> is the fetched entity (if the If-Match header was present
    /// and the precondition succeeded), and <c>Error</c> is an error result if the precondition
    /// failed (not found or ETag mismatch). Both are <c>null</c> when ETag support is disabled
    /// or no If-Match header is present.
    /// </returns>
    internal static async Task<(TEntity? Entity, IResult? Error)> CheckIfMatchPreconditionAsync<TEntity, TKey>(
        HttpContext httpContext,
        IRepository<TEntity, TKey> repository,
        TKey id,
        string entityName,
        RestLibOptions options,
        JsonSerializerOptions jsonOptions,
        CancellationToken ct)
        where TEntity : class
        where TKey : notnull
    {
        if (!options.EnableETagSupport)
        {
            return (null, null);
        }

        var ifMatchHeader = httpContext.Request.Headers.IfMatch;
        if (Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty(ifMatchHeader))
        {
            return (null, null);
        }

        // Get current entity to compare ETags
        var current = await repository.GetByIdAsync(id, ct);
        if (current is null)
        {
            var notFoundResult = Responses.ProblemDetailsResult.NotFound(
                entityName,
                id!,
                httpContext.Request.Path,
                jsonOptions);
            return (null, notFoundResult);
        }

        var etagGenerator = ResolveETagGenerator(httpContext);
        var currentETag = etagGenerator.Generate(current);

        if (!ETagComparer.IfMatchSucceeds(ifMatchHeader, currentETag))
        {
            var preconditionResult = Responses.ProblemDetailsResult.PreconditionFailed(
                "The resource has been modified since you last retrieved it.",
                httpContext.Request.Path,
                jsonOptions);
            return (null, preconditionResult);
        }

        return (current, null);
    }
}
