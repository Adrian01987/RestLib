namespace RestLib.Configuration;

/// <summary>
/// Represents the CLR types resolved for a folder-loaded RestLib resource.
/// </summary>
public sealed record RestLibResolvedResourceTypes
{
    /// <summary>
    /// Gets or sets the API model type used for requests and responses.
    /// </summary>
    public required Type ApiType { get; init; }

    /// <summary>
    /// Gets or sets the optional DB model type. When null, the resource uses the same
    /// type for both API and persistence concerns.
    /// </summary>
    public Type? DbType { get; init; }

    /// <summary>
    /// Gets or sets the route key CLR type.
    /// </summary>
    public required Type KeyType { get; init; }

    /// <summary>
    /// Gets a value indicating whether the resource uses a separate DB model.
    /// </summary>
    public bool HasSeparateDbType => DbType is not null && DbType != ApiType;
}
