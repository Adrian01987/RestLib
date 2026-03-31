using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestLib.Batch;

/// <summary>
/// Represents the raw batch request envelope before action-specific deserialization.
/// </summary>
internal sealed class BatchRequestEnvelope
{
    /// <summary>
    /// Gets or sets the batch action to perform.
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the raw items array as a JSON element for deferred deserialization.
    /// </summary>
    [JsonPropertyName("items")]
    public JsonElement Items { get; set; }
}

/// <summary>
/// Represents a single item in an update or patch batch request.
/// </summary>
/// <typeparam name="TKey">The key type of the entity.</typeparam>
internal sealed class BatchUpdateItem<TKey>
{
    /// <summary>
    /// Gets or sets the entity ID to update or patch.
    /// </summary>
    [JsonPropertyName("id")]
    public TKey Id { get; set; } = default!;

    /// <summary>
    /// Gets or sets the entity body for the update or patch.
    /// </summary>
    [JsonPropertyName("body")]
    public JsonElement Body { get; set; }
}
