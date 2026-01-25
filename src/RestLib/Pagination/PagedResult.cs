namespace RestLib.Pagination;

/// <summary>
/// Represents a paginated result set.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public class PagedResult<T>
{
  /// <summary>
  /// The items in the current page.
  /// </summary>
  public required IReadOnlyList<T> Items { get; init; }

  /// <summary>
  /// The cursor for the next page, if any.
  /// </summary>
  public string? NextCursor { get; init; }

  /// <summary>
  /// Whether there are more items available.
  /// </summary>
  public bool HasMore => NextCursor is not null;
}
