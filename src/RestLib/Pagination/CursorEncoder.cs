using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace RestLib.Pagination;

/// <summary>
/// Encodes and decodes cursor values using base64url encoding.
/// Cursors are opaque strings that hide the underlying pagination state.
/// </summary>
public static class CursorEncoder
{
  /// <summary>
  /// Encodes a cursor value to a base64url string.
  /// </summary>
  /// <typeparam name="T">The type of the cursor value (typically a key type).</typeparam>
  /// <param name="value">The value to encode.</param>
  /// <returns>A base64url encoded string.</returns>
  public static string Encode<T>(T value)
  {
    var payload = new CursorPayload<T> { Value = value };
    var json = JsonSerializer.Serialize(payload);
    var bytes = Encoding.UTF8.GetBytes(json);
    return Base64UrlEncode(bytes);
  }

  /// <summary>
  /// Decodes a base64url cursor string to a value.
  /// </summary>
  /// <typeparam name="T">The expected type of the cursor value.</typeparam>
  /// <param name="cursor">The base64url encoded cursor.</param>
  /// <param name="value">The decoded value if successful.</param>
  /// <returns>True if decoding succeeded; otherwise, false.</returns>
  public static bool TryDecode<T>(string cursor, out T? value)
  {
    value = default;

    if (string.IsNullOrEmpty(cursor))
      return false;

    try
    {
      var bytes = Base64UrlDecode(cursor);
      if (bytes is null)
        return false;

      var json = Encoding.UTF8.GetString(bytes);
      var payload = JsonSerializer.Deserialize<CursorPayload<T>>(json);

      if (payload is null)
        return false;

      value = payload.Value;
      return true;
    }
    catch
    {
      return false;
    }
  }

  /// <summary>
  /// Validates whether a cursor string is a valid base64url encoded cursor.
  /// </summary>
  /// <param name="cursor">The cursor to validate.</param>
  /// <returns>True if the cursor is valid; otherwise, false.</returns>
  public static bool IsValid(string? cursor)
  {
    if (string.IsNullOrEmpty(cursor))
      return true; // null/empty is valid (means first page)

    try
    {
      var bytes = Base64UrlDecode(cursor);
      if (bytes is null)
        return false;

      var json = Encoding.UTF8.GetString(bytes);
      using var doc = JsonDocument.Parse(json);
      return doc.RootElement.TryGetProperty("v", out _);
    }
    catch
    {
      return false;
    }
  }

  /// <summary>
  /// Encodes bytes to a base64url string (RFC 4648 §5).
  /// </summary>
  private static string Base64UrlEncode(byte[] bytes)
  {
    var base64 = Convert.ToBase64String(bytes);
    // Convert to base64url: replace + with -, / with _, and remove padding =
    return base64
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');
  }

  /// <summary>
  /// Decodes a base64url string to bytes (RFC 4648 §5).
  /// </summary>
  private static byte[]? Base64UrlDecode(string base64Url)
  {
    // Convert from base64url: replace - with +, _ with /
    var base64 = base64Url
        .Replace('-', '+')
        .Replace('_', '/');

    // Add padding if necessary
    var paddingLength = (4 - (base64.Length % 4)) % 4;
    base64 = base64.PadRight(base64.Length + paddingLength, '=');

    return Convert.FromBase64String(base64);
  }
}

/// <summary>
/// Cursor payload with short property name for JSON serialization.
/// </summary>
internal class CursorPayload<T>
{
  /// <summary>
  /// The cursor value (v = value).
  /// </summary>
  [System.Text.Json.Serialization.JsonPropertyName("v")]
  public T? Value { get; set; }
}
