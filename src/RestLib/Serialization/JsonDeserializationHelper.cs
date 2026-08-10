using System.Text.Json;
using Microsoft.Extensions.Logging;
using RestLib.Logging;

namespace RestLib.Serialization;

/// <summary>
/// Provides helper methods for JSON deserialization operations.
/// </summary>
internal static class JsonDeserializationHelper
{
    /// <summary>
    /// Attempts to deserialize one JSON element.
    /// </summary>
    /// <typeparam name="T">The type to deserialize each element as.</typeparam>
    /// <param name="element">The JSON element to deserialize.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <param name="item">The deserialized item when successful.</param>
    /// <param name="logger">Optional logger for recording deserialization failures.</param>
    /// <returns><c>true</c> when deserialization succeeds; otherwise, <c>false</c>.</returns>
    internal static bool TryDeserializeItem<T>(
        JsonElement element,
        JsonSerializerOptions jsonOptions,
        out T? item,
        ILogger? logger = null)
    {
        try
        {
            item = element.Deserialize<T>(jsonOptions);
            return true;
        }
        catch (JsonException ex)
        {
            if (logger is not null)
            {
                RestLibLogMessages.JsonDeserializationFailed(logger, ex);
            }

            item = default;
            return false;
        }
    }
}
