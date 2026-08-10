using System.Text.Json.Serialization;
using RestLib.Responses;

namespace RestLib.Batch;

/// <summary>
/// Represents the batch response envelope containing per-item results.
/// Once a request envelope and its items array are accepted, the envelope
/// contains one result per array member in the same order as the request,
/// including members that could not be deserialized.
/// </summary>
public class BatchResponse
{
    /// <summary>
    /// Gets or sets the per-item results in original request order.
    /// Each entry's <see cref="BatchItemResult.Index"/> identifies the
    /// corresponding zero-based request position.
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
    /// In a per-item batch response, this also matches the entry's position in
    /// <see cref="BatchResponse.Items"/>.
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
