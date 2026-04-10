namespace RestLib.Hypermedia;

/// <summary>
/// Provides custom hypermedia links for entities.
/// Implement this interface and register it with DI to add custom links
/// (e.g., related resources, external references) to entity responses
/// when HATEOAS is enabled.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
/// <remarks>
/// Custom links are appended after the standard CRUD links (<c>self</c>,
/// <c>collection</c>, <c>update</c>, <c>patch</c>, <c>delete</c>).
/// If a custom link uses the same relation name as a standard link,
/// the custom link takes precedence.
/// </remarks>
/// <example>
/// <code>
/// public class ProductLinkProvider : IHateoasLinkProvider&lt;Product, Guid&gt;
/// {
///     public IReadOnlyDictionary&lt;string, HateoasLink&gt;? GetLinks(Product entity, Guid key)
///     {
///         return new Dictionary&lt;string, HateoasLink&gt;
///         {
///             ["category"] = new HateoasLink
///             {
///                 Href = $"/api/categories/{entity.CategoryId}"
///             },
///             ["reviews"] = new HateoasLink
///             {
///                 Href = $"/api/products/{key}/reviews"
///             }
///         };
///     }
/// }
/// </code>
/// </example>
public interface IHateoasLinkProvider<in TEntity, in TKey>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Returns custom links for the given entity.
    /// Return <c>null</c> to add no custom links for this entity.
    /// </summary>
    /// <param name="entity">The entity being serialized.</param>
    /// <param name="key">The entity key.</param>
    /// <returns>A dictionary of link relation names to links, or <c>null</c>.</returns>
    IReadOnlyDictionary<string, HateoasLink>? GetLinks(TEntity entity, TKey key);
}
