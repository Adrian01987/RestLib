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
/// Represents one ordered member of an accepted batch items array.
/// </summary>
internal sealed class BatchItemInput
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BatchItemInput"/> class.
    /// </summary>
    /// <param name="jsonValue">The raw JSON value, when retained.</param>
    /// <param name="deserializedValue">The action-normalized CLR value, when available.</param>
    /// <param name="hasDeserializedValue">Whether a CLR value is available.</param>
    /// <param name="hasDeserializationError">Whether action-specific preprocessing failed.</param>
    private BatchItemInput(
        JsonElement jsonValue,
        object? deserializedValue,
        bool hasDeserializedValue,
        bool hasDeserializationError)
    {
        JsonValue = jsonValue;
        DeserializedValue = deserializedValue;
        HasDeserializedValue = hasDeserializedValue;
        HasDeserializationError = hasDeserializationError;
    }

    /// <summary>
    /// Gets the raw JSON value.
    /// </summary>
    internal JsonElement JsonValue { get; }

    /// <summary>
    /// Gets the action-normalized CLR value.
    /// </summary>
    internal object? DeserializedValue { get; }

    /// <summary>
    /// Gets a value indicating whether an action-normalized CLR value is available.
    /// </summary>
    internal bool HasDeserializedValue { get; }

    /// <summary>
    /// Gets a value indicating whether action-specific preprocessing failed.
    /// </summary>
    internal bool HasDeserializationError { get; }

    /// <summary>
    /// Creates an input whose raw JSON still requires model deserialization.
    /// </summary>
    /// <param name="value">The raw JSON value.</param>
    /// <returns>The batch item input.</returns>
    internal static BatchItemInput FromJson(JsonElement value) =>
        new(value, deserializedValue: null, hasDeserializedValue: false, hasDeserializationError: false);

    /// <summary>
    /// Creates an input that has already been normalized to its action-specific CLR type.
    /// </summary>
    /// <param name="value">The normalized CLR value.</param>
    /// <returns>The batch item input.</returns>
    internal static BatchItemInput FromDeserialized(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new BatchItemInput(
            default,
            value,
            hasDeserializedValue: true,
            hasDeserializationError: false);
    }

    /// <summary>
    /// Creates an input whose action-specific preprocessing failed.
    /// </summary>
    /// <returns>The batch item input.</returns>
    internal static BatchItemInput FromDeserializationError() =>
        new(
            default,
            deserializedValue: null,
            hasDeserializedValue: false,
            hasDeserializationError: true);
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
