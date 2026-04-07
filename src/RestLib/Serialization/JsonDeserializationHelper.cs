using System.Text.Json;

namespace RestLib.Serialization;

/// <summary>
/// Provides helper methods for JSON deserialization operations.
/// </summary>
internal static class JsonDeserializationHelper
{
    /// <summary>
    /// Deserializes a JSON array element into a list of items.
    /// Returns <c>null</c> if the element is not a JSON array or cannot be deserialized.
    /// </summary>
    /// <typeparam name="T">The type to deserialize each element as.</typeparam>
    /// <param name="element">The raw JSON element to deserialize.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <returns>A list of deserialized items, or <c>null</c> on failure.</returns>
    internal static IReadOnlyList<T?>? DeserializeArray<T>(
        JsonElement element,
        JsonSerializerOptions jsonOptions)
    {
        try
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return element.Deserialize<List<T?>>(jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
