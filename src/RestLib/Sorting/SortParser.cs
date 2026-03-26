namespace RestLib.Sorting;

/// <summary>
/// Parses and validates the sort query parameter.
/// </summary>
public static class SortParser
{
  /// <summary>
  /// Parses a sort query parameter value into validated sort fields.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <param name="sortValue">The raw sort query parameter value (e.g., "price:asc,name:desc").</param>
  /// <param name="configuration">The sort configuration defining allowed fields.</param>
  /// <returns>A parse result containing fields and any errors.</returns>
  public static SortParseResult Parse<TEntity>(
      string? sortValue,
      SortConfiguration<TEntity> configuration)
      where TEntity : class
  {
    if (string.IsNullOrWhiteSpace(sortValue))
    {
      return new SortParseResult();
    }

    var fields = new List<SortField>();
    var errors = new List<SortValidationError>();
    var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var allowedNames = string.Join(", ", configuration.Properties.Select(p => p.QueryParameterName));

    var segments = sortValue.Split(',');

    foreach (var segment in segments)
    {
      var trimmed = segment.Trim();
      if (string.IsNullOrEmpty(trimmed))
      {
        continue; // Skip empty segments (e.g., trailing comma)
      }

      var parts = trimmed.Split(':', 2);
      var fieldName = parts[0].Trim();
      var directionStr = parts.Length > 1 ? parts[1].Trim() : "asc";

      // Look up the field in the configuration
      var property = configuration.FindByQueryName(fieldName);
      if (property is null)
      {
        errors.Add(new SortValidationError
        {
          Field = fieldName,
          Message = $"'{fieldName}' is not a sortable field. Allowed fields: {allowedNames}."
        });
        continue;
      }

      // Parse direction
      SortDirection direction;
      if (string.Equals(directionStr, "asc", StringComparison.OrdinalIgnoreCase))
      {
        direction = SortDirection.Asc;
      }
      else if (string.Equals(directionStr, "desc", StringComparison.OrdinalIgnoreCase))
      {
        direction = SortDirection.Desc;
      }
      else
      {
        errors.Add(new SortValidationError
        {
          Field = fieldName,
          Message = "Direction must be 'asc' or 'desc'."
        });
        continue;
      }

      // Check for duplicates
      if (!seenFields.Add(property.QueryParameterName))
      {
        errors.Add(new SortValidationError
        {
          Field = fieldName,
          Message = "Duplicate sort field."
        });
        continue;
      }

      fields.Add(new SortField
      {
        PropertyName = property.PropertyName,
        QueryParameterName = property.QueryParameterName,
        Direction = direction
      });
    }

    return new SortParseResult
    {
      Fields = fields,
      Errors = errors
    };
  }
}
