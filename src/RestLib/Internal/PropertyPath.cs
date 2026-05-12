namespace RestLib.Internal;

/// <summary>
/// Represents a validated scalar property path for query configuration.
/// </summary>
internal sealed class PropertyPath
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyPath"/> class.
    /// </summary>
    /// <param name="clrPath">The CLR path using property names.</param>
    /// <param name="queryPath">The query path using snake_case segments.</param>
    /// <param name="leafPropertyType">The leaf CLR property type.</param>
    /// <param name="clrSegments">The CLR path segments.</param>
    /// <param name="querySegments">The query path segments.</param>
    /// <param name="hasCollectionSegment">
    /// A value indicating whether any segment resolved to a collection-valued property.
    /// </param>
    public PropertyPath(
        string clrPath,
        string queryPath,
        Type leafPropertyType,
        IReadOnlyList<string> clrSegments,
        IReadOnlyList<string> querySegments,
        bool hasCollectionSegment)
    {
        ClrPath = clrPath;
        QueryPath = queryPath;
        LeafPropertyType = leafPropertyType;
        ClrSegments = clrSegments.ToArray();
        QuerySegments = querySegments.ToArray();
        HasCollectionSegment = hasCollectionSegment;
    }

    /// <summary>
    /// Gets the CLR path using property names.
    /// </summary>
    public string ClrPath { get; }

    /// <summary>
    /// Gets the query path using snake_case segments.
    /// </summary>
    public string QueryPath { get; }

    /// <summary>
    /// Gets the leaf CLR property type.
    /// </summary>
    public Type LeafPropertyType { get; }

    /// <summary>
    /// Gets the CLR path segments.
    /// </summary>
    public IReadOnlyList<string> ClrSegments { get; }

    /// <summary>
    /// Gets the query path segments.
    /// </summary>
    public IReadOnlyList<string> QuerySegments { get; }

    /// <summary>
    /// Gets a value indicating whether any segment resolved to a collection-valued property.
    /// </summary>
    public bool HasCollectionSegment { get; }

    /// <summary>
    /// Gets a value indicating whether the path resolves to a direct property.
    /// </summary>
    public bool IsDirect => ClrSegments.Count == 1;
}
