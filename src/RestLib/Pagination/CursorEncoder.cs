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
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
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
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException
            or InvalidOperationException or NotSupportedException)
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
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException
            or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Encodes bytes to a base64url string (RFC 4648 §5).
    /// Uses span replacement to avoid intermediate string allocations.
    /// </summary>
    private static string Base64UrlEncode(byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        var len = base64.AsSpan().TrimEnd('=').Length;
        return string.Create(len, (base64, len), static (span, state) =>
        {
            state.base64.AsSpan(0, state.len).CopyTo(span);
            span.Replace('+', '-');
            span.Replace('/', '_');
        });
    }

    /// <summary>
    /// Decodes a base64url string to bytes (RFC 4648 §5).
    /// Uses a rented char array to avoid intermediate string allocations.
    /// </summary>
    private static byte[]? Base64UrlDecode(string base64Url)
    {
        // Calculate the padded length
        var paddingLength = (4 - (base64Url.Length % 4)) % 4;
        var totalLength = base64Url.Length + paddingLength;

        var buffer = System.Buffers.ArrayPool<char>.Shared.Rent(totalLength);
        try
        {
            base64Url.AsSpan().CopyTo(buffer);

            // Replace base64url characters with standard base64
            var span = buffer.AsSpan(0, base64Url.Length);
            span.Replace('-', '+');
            span.Replace('_', '/');

            // Add padding
            for (var i = base64Url.Length; i < totalLength; i++)
            {
                buffer[i] = '=';
            }

            return Convert.FromBase64CharArray(buffer, 0, totalLength);
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(buffer);
        }
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
