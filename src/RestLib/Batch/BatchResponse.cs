using System.Text.Json.Serialization;
using RestLib.Responses;

namespace RestLib.Batch;

/// <summary>
/// Represents the batch response envelope containing per-item results.
/// </summary>
public class BatchResponse
{
    /// <summary>
    /// Gets or sets the per-item results.
    /// </summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<BatchItemResult> Items { get; init; }
}

/// <summary>
/// Represents the result of processing a single item in a batch request.
/// </summary>
public class BatchItemResult
{
    /// <summary>
    /// Gets or sets the zero-based index of this item in the original request.
    /// </summary>
    [JsonPropertyName("index")]
    public required int Index { get; init; }

    /// <summary>
    /// Gets or sets the HTTP status code for this item.
    /// </summary>
    [JsonPropertyName("status")]
    public required int Status { get; init; }

    /// <summary>
    /// Gets or sets the entity, if the operation succeeded and returns an entity.
    /// Null for delete operations and failed items.
    /// </summary>
    [JsonPropertyName("entity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Entity { get; init; }

    /// <summary>
    /// Gets or sets the error details, if the operation failed.
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RestLibProblemDetails? Error { get; init; }
}
