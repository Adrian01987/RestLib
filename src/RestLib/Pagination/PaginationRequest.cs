using RestLib.Filtering;
using RestLib.Sorting;

namespace RestLib.Pagination;

/// <summary>
/// Represents a pagination request with optional filters and sort fields.
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

  /// <summary>
  /// Gets the sort fields to apply to the query.
  /// Repository implementations should use these to order results.
  /// </summary>
  public IReadOnlyList<SortField> SortFields { get; init; } = [];
}
