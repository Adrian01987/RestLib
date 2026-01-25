namespace RestLib.Abstractions;

/// <summary>
/// Generates ETags for resources per RFC 9110.
/// ETags are used for caching and optimistic concurrency control.
/// </summary>
/// <remarks>
/// Per RFC 9110 Section 8.8.3, an ETag is an opaque quoted string that
/// uniquely identifies a specific version of a resource.
/// </remarks>
public interface IETagGenerator
{
  /// <summary>
  /// Generates an ETag for the given entity.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <param name="entity">The entity to generate an ETag for.</param>
  /// <returns>The ETag value including quotes (e.g., "abc123" or W/"abc123" for weak).</returns>
  string Generate<TEntity>(TEntity entity) where TEntity : class;

  /// <summary>
  /// Validates if the provided ETag matches the entity's current ETag.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <param name="entity">The entity to validate against.</param>
  /// <param name="etag">The ETag value to validate (including quotes).</param>
  /// <returns>True if the ETags match; otherwise, false.</returns>
  bool Validate<TEntity>(TEntity entity, string etag) where TEntity : class;
}
