using RestLib.Filtering;

namespace RestLib.Pagination;

/// <summary>
/// Represents a pagination request with optional filters.
/// </summary>
public class PaginationRequest
{
  /// <summary>
  /// The cursor for the current position.
  /// </summary>
  public string? Cursor { get; init; }

  /// <summary>
  /// The maximum number of items to return.
  /// </summary>
  public int Limit { get; init; } = 20;

  /// <summary>
  /// Gets the filter values to apply to the query.
  /// Repository implementations should use these to filter results.
  /// </summary>
  public IReadOnlyList<FilterValue> Filters { get; init; } = [];
}
