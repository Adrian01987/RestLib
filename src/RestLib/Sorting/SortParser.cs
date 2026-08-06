using RestLib.Internal;

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
        var result = ConfiguredQueryListParser.Parse<SortPropertyConfiguration, SortField, SortValidationError>(
            sortValue,
            configuration.Properties,
            static property => property.QueryParameterName,
            static segment =>
            {
                var parts = segment.Split(':', 2);
                return new ConfiguredQueryToken(
                    parts[0].Trim(),
                    parts.Length > 1 ? parts[1].Trim() : null);
            },
            static (token, property) =>
            {
                var directionValue = token.Modifier ?? "asc";
                SortDirection direction;
                if (string.Equals(directionValue, "asc", StringComparison.OrdinalIgnoreCase))
                {
                    direction = SortDirection.Asc;
                }
                else if (string.Equals(directionValue, "desc", StringComparison.OrdinalIgnoreCase))
                {
                    direction = SortDirection.Desc;
                }
                else
                {
                    return new ConfiguredQueryItemParseResult<SortField, SortValidationError>(
                        null,
                        new SortValidationError
                        {
                            Field = token.FieldName,
                            Message = "Direction must be 'asc' or 'desc'."
                        });
                }

                return new ConfiguredQueryItemParseResult<SortField, SortValidationError>(
                    new SortField
                    {
                        PropertyName = property.PropertyName,
                        QueryParameterName = property.QueryParameterName,
                        Direction = direction
                    },
                    null);
            },
            static (fieldName, allowedNames) => new SortValidationError
            {
                Field = fieldName,
                Message = $"'{fieldName}' is not a sortable field. Allowed fields: {allowedNames}."
            },
            static fieldName => new SortValidationError
            {
                Field = fieldName,
                Message = "Duplicate sort field."
            });

        return new SortParseResult
        {
            Fields = result.Items,
            Errors = result.Errors
        };
    }
}
