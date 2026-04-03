using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RestLib.Abstractions;

namespace RestLib.Caching;

/// <summary>
/// Default ETag generator using SHA-256 hash of the JSON-serialized entity.
/// Produces strong ETags per RFC 9110.
/// </summary>
/// <remarks>
/// This implementation serializes the entity to JSON and computes a SHA-256 hash.
/// The hash is then encoded as base64url and wrapped in quotes per RFC 9110.
///
/// The ETag will change whenever any property of the entity changes, making it
/// suitable for detecting modifications.
/// </remarks>
public class HashBasedETagGenerator : IETagGenerator
{
  private readonly JsonSerializerOptions _jsonOptions;

  /// <summary>
  /// Initializes a new instance with default JSON options.
  /// </summary>
  public HashBasedETagGenerator()
      : this(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
  {
  }

  /// <summary>
  /// Initializes a new instance with custom JSON options.
  /// </summary>
  /// <param name="jsonOptions">The JSON serializer options to use.</param>
  public HashBasedETagGenerator(JsonSerializerOptions jsonOptions)
  {
    _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
  }

  /// <inheritdoc />
  public string Generate<TEntity>(TEntity entity) where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(entity);

    var json = JsonSerializer.Serialize(entity, _jsonOptions);
    var hash = ComputeHash(json);
    var encoded = EncodeToBase64Url(hash);

    // RFC 9110: ETags are quoted strings
    return $"\"{encoded}\"";
  }

  /// <inheritdoc />
  public bool Validate<TEntity>(TEntity entity, string etag) where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(entity);

    if (string.IsNullOrEmpty(etag))
    {
      return false;
    }

    // Handle wildcard
    if (etag == "*")
    {
      return true;
    }

    // Handle weak ETags (W/"...")
    var normalizedEtag = etag;
    if (etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
    {
      normalizedEtag = etag[2..];
    }

    var currentEtag = Generate(entity);
    return string.Equals(currentEtag, normalizedEtag, StringComparison.Ordinal);
  }

  /// <summary>
  /// Computes the SHA-256 hash of the input string.
  /// </summary>
  private static byte[] ComputeHash(string input)
  {
    var bytes = Encoding.UTF8.GetBytes(input);
    return SHA256.HashData(bytes);
  }

  /// <summary>
  /// Encodes bytes to base64url (URL-safe base64 without padding).
  /// </summary>
  private static string EncodeToBase64Url(byte[] bytes)
  {
    // Use first 16 bytes (128 bits) for a shorter but still unique ETag
    var truncated = bytes[..16];
    return Convert.ToBase64String(truncated)
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');
  }
}
