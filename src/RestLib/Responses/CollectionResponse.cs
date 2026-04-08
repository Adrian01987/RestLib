using System.Text.Json.Serialization;

namespace RestLib.Responses;

/// <summary>
/// Represents a standardized collection response per Zalando Rule 110.
/// Collections are always wrapped in an object with an "items" array.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public class CollectionResponse<T>
{
    /// <summary>
    /// The items in the current page.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// The total number of entities matching the current filters.
    /// Only present when the repository implements
    /// <see cref="Abstractions.ICountableRepository{TEntity, TKey}"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalCount { get; init; }

    /// <summary>
    /// The URL to the current page (self link).
    /// </summary>
    public string? Self { get; init; }

    /// <summary>
    /// The URL to the first page.
    /// Per Zalando Rule 161, the first link allows navigation to the beginning.
    /// </summary>
    public string? First { get; init; }

    /// <summary>
    /// The URL to the next page, if available.
    /// </summary>
    public string? Next { get; init; }

    /// <summary>
    /// The URL to the previous page, if available.
    /// </summary>
    public string? Prev { get; init; }
}
