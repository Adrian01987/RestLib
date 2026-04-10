using Microsoft.AspNetCore.Http;
using RestLib.Configuration;

namespace RestLib.Hypermedia;

/// <summary>
/// Builds HAL-style <c>_links</c> dictionaries for entity responses.
/// Generates links conditioned on which operations are enabled in the configuration.
/// </summary>
internal static class HateoasLinkBuilder
{
    /// <summary>
    /// Builds the <c>_links</c> dictionary for a single entity.
    /// Includes <c>self</c>, <c>collection</c>, and CRUD operation links
    /// conditioned on <see cref="RestLibEndpointConfiguration{TEntity, TKey}.IsOperationEnabled"/>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="request">The current HTTP request (for scheme and host).</param>
    /// <param name="collectionPath">The collection route path (e.g., "/api/products").</param>
    /// <param name="entityKey">The entity key value.</param>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="customLinks">Optional custom links to include.</param>
    /// <returns>A dictionary of link relation names to link objects.</returns>
    internal static Dictionary<string, HateoasLink> BuildEntityLinks<TEntity, TKey>(
        HttpRequest request,
        string collectionPath,
        TKey entityKey,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        IReadOnlyDictionary<string, HateoasLink>? customLinks = null)
        where TEntity : class
        where TKey : notnull
    {
        var baseUrl = $"{request.Scheme}://{request.Host}";
        var entityUrl = $"{baseUrl}{collectionPath}/{entityKey}";
        var collectionUrl = $"{baseUrl}{collectionPath}";

        var links = new Dictionary<string, HateoasLink>
        {
            ["self"] = new HateoasLink { Href = entityUrl }
        };

        // Collection link
        if (config.IsOperationEnabled(RestLibOperation.GetAll))
        {
            links["collection"] = new HateoasLink { Href = collectionUrl };
        }

        // CRUD operation links (only for enabled operations)
        if (config.IsOperationEnabled(RestLibOperation.Update))
        {
            links["update"] = new HateoasLink { Href = entityUrl, Method = "PUT" };
        }

        if (config.IsOperationEnabled(RestLibOperation.Patch))
        {
            links["patch"] = new HateoasLink { Href = entityUrl, Method = "PATCH" };
        }

        if (config.IsOperationEnabled(RestLibOperation.Delete))
        {
            links["delete"] = new HateoasLink { Href = entityUrl, Method = "DELETE" };
        }

        // Append custom links (user-provided via IHateoasLinkProvider)
        if (customLinks is not null)
        {
            foreach (var (rel, link) in customLinks)
            {
                links[rel] = link;
            }
        }

        return links;
    }

    /// <summary>
    /// Extracts the collection path from the current request path for entity-specific endpoints.
    /// For GetById/Update/Patch/Delete requests (path ends with <c>/{id}</c>), strips the
    /// last segment to get the collection path. For collection-level requests (GetAll, Create),
    /// returns the path as-is.
    /// </summary>
    /// <param name="requestPath">The full request path.</param>
    /// <param name="isCollectionEndpoint">Whether this is a collection-level endpoint (GetAll, Create).</param>
    /// <returns>The collection path.</returns>
    internal static string GetCollectionPath(string requestPath, bool isCollectionEndpoint)
    {
        if (isCollectionEndpoint)
        {
            return requestPath;
        }

        // Strip the last segment (the entity ID) to get the collection path
        var lastSlash = requestPath.LastIndexOf('/');
        return lastSlash > 0 ? requestPath[..lastSlash] : requestPath;
    }
}
