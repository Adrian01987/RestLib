using System.Text.Json.Serialization;

namespace RestLib.Responses;

/// <summary>
/// RFC 9457 Problem Details response for error handling.
/// </summary>
public class RestLibProblemDetails
{
  /// <summary>
  /// A URI reference that identifies the problem type.
  /// </summary>
  [JsonPropertyName("type")]
  public required string Type { get; init; }

  /// <summary>
  /// A short, human-readable summary of the problem type.
  /// </summary>
  [JsonPropertyName("title")]
  public required string Title { get; init; }

  /// <summary>
  /// The HTTP status code.
  /// </summary>
  [JsonPropertyName("status")]
  public required int Status { get; init; }

  /// <summary>
  /// A human-readable explanation specific to this occurrence.
  /// </summary>
  [JsonPropertyName("detail")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Detail { get; init; }

  /// <summary>
  /// A URI reference that identifies the specific occurrence.
  /// </summary>
  [JsonPropertyName("instance")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Instance { get; init; }

  /// <summary>
  /// Validation errors keyed by field name (snake_case).
  /// </summary>
  [JsonPropertyName("errors")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public IDictionary<string, string[]>? Errors { get; init; }
}
