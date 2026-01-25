using System.Text.Json;
using System.Text.Json.Serialization;
using RestLib.Configuration;

namespace RestLib.Serialization;

/// <summary>
/// Provides JSON serializer options configured according to RestLib settings.
/// Implements Zalando Rule 118 (snake_case) by default.
/// </summary>
public static class RestLibJsonOptions
{
  /// <summary>
  /// Creates JSON serializer options based on RestLib configuration.
  /// </summary>
  /// <param name="options">The RestLib options.</param>
  /// <returns>Configured JsonSerializerOptions.</returns>
  public static JsonSerializerOptions Create(RestLibOptions options)
  {
    ArgumentNullException.ThrowIfNull(options);

    var jsonOptions = new JsonSerializerOptions
    {
      PropertyNamingPolicy = options.JsonNamingPolicy,
      PropertyNameCaseInsensitive = true,
      DefaultIgnoreCondition = options.OmitNullValues
            ? JsonIgnoreCondition.WhenWritingNull
            : JsonIgnoreCondition.Never
    };

    return jsonOptions;
  }

  /// <summary>
  /// Creates default JSON serializer options with snake_case naming and null omission.
  /// </summary>
  /// <returns>Default JsonSerializerOptions for RestLib.</returns>
  public static JsonSerializerOptions CreateDefault()
  {
    return Create(new RestLibOptions());
  }
}
