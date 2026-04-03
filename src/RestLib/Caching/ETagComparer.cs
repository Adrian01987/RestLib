using Microsoft.Extensions.Primitives;

namespace RestLib.Caching;

/// <summary>
/// Helper class for parsing and comparing ETags according to RFC 9110.
/// </summary>
public static class ETagComparer
{
  /// <summary>
  /// Determines if any of the provided ETags match the current ETag.
  /// Used for If-Match validation (strong comparison).
  /// </summary>
  /// <param name="headerValues">The ETag values from the If-Match header.</param>
  /// <param name="currentETag">The current ETag of the resource.</param>
  /// <returns>True if any ETag matches or wildcard is present; otherwise, false.</returns>
  public static bool IfMatchSucceeds(StringValues headerValues, string currentETag)
  {
    if (StringValues.IsNullOrEmpty(headerValues))
    {
      return true; // No If-Match header means precondition is satisfied
    }

    var etags = ParseETags(headerValues);

    // Wildcard matches any existing resource
    if (etags.Contains("*"))
    {
      return true;
    }

    // RFC 9110: If-Match uses strong comparison
    // Strong comparison: both must be strong ETags and byte-for-byte identical
    foreach (var etag in etags)
    {
      if (StrongComparison(etag, currentETag))
      {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// Determines if the If-None-Match condition succeeds (resource should be returned).
  /// Used for conditional GET requests.
  /// </summary>
  /// <param name="headerValues">The ETag values from the If-None-Match header.</param>
  /// <param name="currentETag">The current ETag of the resource.</param>
  /// <returns>True if the resource should be returned; false if 304 should be returned.</returns>
  public static bool IfNoneMatchSucceeds(StringValues headerValues, string currentETag)
  {
    if (StringValues.IsNullOrEmpty(headerValues))
    {
      return true; // No If-None-Match header means return the resource
    }

    var etags = ParseETags(headerValues);

    // Wildcard matches any existing resource - return 304
    if (etags.Contains("*"))
    {
      return false;
    }

    // RFC 9110: If-None-Match uses weak comparison for GET/HEAD
    // If any ETag matches, return 304 (condition fails)
    foreach (var etag in etags)
    {
      if (WeakComparison(etag, currentETag))
      {
        return false; // Match found, return 304
      }
    }

    return true; // No match, return the resource
  }

  /// <summary>
  /// Parses ETag header values into individual ETags.
  /// Handles comma-separated lists and multiple header values.
  /// </summary>
  private static HashSet<string> ParseETags(StringValues headerValues)
  {
    var etags = new HashSet<string>(StringComparer.Ordinal);

    foreach (var value in headerValues)
    {
      if (string.IsNullOrWhiteSpace(value))
      {
        continue;
      }

      // Handle comma-separated ETags within a single header value
      // e.g., "\"etag1\", \"etag2\", W/\"etag3\""
      var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

      foreach (var part in parts)
      {
        var trimmed = part.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
          etags.Add(trimmed);
        }
      }
    }

    return etags;
  }

  /// <summary>
  /// Performs strong comparison per RFC 9110 Section 8.8.3.2.
  /// Both ETags must be strong and byte-for-byte identical.
  /// </summary>
  private static bool StrongComparison(string etag1, string etag2)
  {
    // Weak ETags fail strong comparison
    if (IsWeakETag(etag1) || IsWeakETag(etag2))
    {
      return false;
    }

    return string.Equals(etag1, etag2, StringComparison.Ordinal);
  }

  /// <summary>
  /// Performs weak comparison per RFC 9110 Section 8.8.3.2.
  /// Compares the opaque-tag portion, ignoring the weak indicator.
  /// </summary>
  private static bool WeakComparison(string etag1, string etag2)
    => string.Equals(GetOpaqueTag(etag1), GetOpaqueTag(etag2), StringComparison.Ordinal);

  /// <summary>
  /// Extracts the opaque-tag portion from an ETag, stripping the weak indicator if present.
  /// </summary>
  private static string GetOpaqueTag(string etag)
  {
    if (IsWeakETag(etag))
    {
      return etag[2..]; // Strip "W/" prefix
    }

    return etag;
  }

  /// <summary>
  /// Determines if an ETag is a weak ETag (prefixed with W/).
  /// </summary>
  private static bool IsWeakETag(string etag)
    => etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase);
}
