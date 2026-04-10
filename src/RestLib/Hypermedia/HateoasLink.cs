using System.Text.Json.Serialization;

namespace RestLib.Hypermedia;

/// <summary>
/// Represents a single hypermedia link in the HAL-style <c>_links</c> object.
/// </summary>
public class HateoasLink
{
    /// <summary>
    /// Gets the target URI of the link.
    /// </summary>
    [JsonPropertyName("href")]
    public required string Href { get; init; }

    /// <summary>
    /// Gets the HTTP method associated with the link, if applicable.
    /// Omitted for GET links (the default assumption in HAL).
    /// </summary>
    [JsonPropertyName("method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Method { get; init; }
}
