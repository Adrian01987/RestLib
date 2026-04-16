using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// EF Core adapter cursor payload for keyset pagination.
/// </summary>
internal sealed class EfCoreKeysetCursor
{
    /// <summary>
    /// Gets or sets the cursor payload version.
    /// </summary>
    [JsonPropertyName("ver")]
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the sort signature used to validate cursor reuse.
    /// </summary>
    [JsonPropertyName("sig")]
    public string SortSignature { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ordered sort values captured from the last item.
    /// </summary>
    [JsonPropertyName("vals")]
    public List<JsonElement> Values { get; set; } = [];
}
