using System.Text.Json;
using RestLib.Configuration;
using RestLib.Endpoints;
using RestLib.FieldSelection;

namespace RestLib.Hypermedia;

/// <summary>
/// Helper methods for injecting <c>_links</c> into entity responses.
/// Handles both regular entities and field-selected (projected) dictionaries.
/// </summary>
internal static class HateoasHelper
{
    /// <summary>
    /// The JSON property name for the HATEOAS links object.
    /// Uses underscore prefix following HAL convention.
    /// </summary>
    internal const string LinksPropertyName = "_links";

    /// <summary>
    /// Converts an entity to a <see cref="Dictionary{String, JsonElement}"/> with
    /// <c>_links</c> injected as a property. Used for single-entity responses
    /// (GetById, Create, Update, Patch) when HATEOAS is enabled.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="entity">The entity to serialize.</param>
    /// <param name="links">The links dictionary to inject.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <returns>A dictionary containing the entity properties plus <c>_links</c>.</returns>
    internal static Dictionary<string, JsonElement> EntityWithLinks<TEntity, TKey>(
        TEntity entity,
        Dictionary<string, HateoasLink> links,
        JsonSerializerOptions jsonOptions)
        where TEntity : class
        where TKey : notnull
    {
        var json = JsonSerializer.Serialize(entity, jsonOptions);
        using var doc = JsonDocument.Parse(json);

        var result = new Dictionary<string, JsonElement>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        result[LinksPropertyName] = JsonSerializer.SerializeToElement(links, jsonOptions);

        return result;
    }

    /// <summary>
    /// Injects <c>_links</c> into an already-projected field selection dictionary.
    /// </summary>
    /// <param name="projected">The projected dictionary of selected fields.</param>
    /// <param name="links">The links dictionary to inject.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    internal static void InjectLinksIntoProjected(
        Dictionary<string, JsonElement> projected,
        Dictionary<string, HateoasLink> links,
        JsonSerializerOptions jsonOptions)
    {
        projected[LinksPropertyName] = JsonSerializer.SerializeToElement(links, jsonOptions);
    }

    /// <summary>
    /// Builds per-item <c>_links</c> and injects them into each projected collection item.
    /// The entity key is extracted from the original (non-projected) entities.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="projectedItems">The projected items to inject links into.</param>
    /// <param name="originalEntities">The original entities (used for key extraction).</param>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="request">The current HTTP request.</param>
    /// <param name="collectionPath">The collection route path.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="customLinksProvider">Optional custom link provider.</param>
    internal static void InjectLinksIntoProjectedCollection<TEntity, TKey>(
        IReadOnlyList<Dictionary<string, JsonElement>> projectedItems,
        IReadOnlyList<TEntity> originalEntities,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        Microsoft.AspNetCore.Http.HttpRequest request,
        string collectionPath,
        JsonSerializerOptions jsonOptions,
        IHateoasLinkProvider<TEntity, TKey>? customLinksProvider = null)
        where TEntity : class
        where TKey : notnull
    {
        for (var i = 0; i < projectedItems.Count && i < originalEntities.Count; i++)
        {
            var entityKey = EntityKeyHelper.GetEntityKey(originalEntities[i], config.KeySelector);
            if (entityKey is null)
            {
                continue;
            }

            var customLinks = customLinksProvider?.GetLinks(originalEntities[i], entityKey);
            var links = HateoasLinkBuilder.BuildEntityLinks(request, collectionPath, entityKey, config, customLinks);
            InjectLinksIntoProjected(projectedItems[i], links, jsonOptions);
        }
    }

    /// <summary>
    /// Wraps each entity in a collection into a dictionary with <c>_links</c>.
    /// Used when field selection is NOT active but HATEOAS is enabled for GetAll.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="entities">The entities to wrap.</param>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="request">The current HTTP request.</param>
    /// <param name="collectionPath">The collection route path.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="customLinksProvider">Optional custom link provider.</param>
    /// <returns>A list of dictionaries, each containing entity properties plus <c>_links</c>.</returns>
    internal static IReadOnlyList<Dictionary<string, JsonElement>> WrapCollectionWithLinks<TEntity, TKey>(
        IReadOnlyList<TEntity> entities,
        RestLibEndpointConfiguration<TEntity, TKey> config,
        Microsoft.AspNetCore.Http.HttpRequest request,
        string collectionPath,
        JsonSerializerOptions jsonOptions,
        IHateoasLinkProvider<TEntity, TKey>? customLinksProvider = null)
        where TEntity : class
        where TKey : notnull
    {
        var results = new List<Dictionary<string, JsonElement>>(entities.Count);

        foreach (var entity in entities)
        {
            var entityKey = EntityKeyHelper.GetEntityKey(entity, config.KeySelector);
            if (entityKey is null)
            {
                continue;
            }

            var customLinks = customLinksProvider?.GetLinks(entity, entityKey);
            var links = HateoasLinkBuilder.BuildEntityLinks(request, collectionPath, entityKey, config, customLinks);
            var wrapped = EntityWithLinks<TEntity, TKey>(entity, links, jsonOptions);
            results.Add(wrapped);
        }

        return results;
    }
}
