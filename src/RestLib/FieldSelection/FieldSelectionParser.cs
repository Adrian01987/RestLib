namespace RestLib.FieldSelection;

/// <summary>
/// Parses and validates the fields query parameter.
/// </summary>
public static class FieldSelectionParser
{
    /// <summary>
    /// Parses a fields query parameter value into validated selected fields.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="fieldsValue">The raw fields query parameter value (e.g., "id,name,price").</param>
    /// <param name="configuration">The field selection configuration defining allowed fields.</param>
    /// <returns>A parse result containing fields and any errors.</returns>
    public static FieldSelectionParseResult Parse<TEntity>(
        string? fieldsValue,
        FieldSelectionConfiguration<TEntity> configuration)
        where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(fieldsValue))
        {
            return new FieldSelectionParseResult();
        }

        var fields = new List<SelectedField>();
        var errors = new List<FieldSelectionValidationError>();
        var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedNames = string.Join(", ",
            configuration.Properties.Select(p => p.QueryFieldName));

        var segments = fieldsValue.Split(',');

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue; // Skip empty segments (e.g., trailing comma)
            }

            // Reject duplicates
            if (!seenFields.Add(trimmed))
            {
                errors.Add(new FieldSelectionValidationError
                {
                    Field = trimmed,
                    Message = "Duplicate field."
                });
                continue;
            }

            var property = configuration.FindByQueryName(trimmed);
            if (property is null)
            {
                errors.Add(new FieldSelectionValidationError
                {
                    Field = trimmed,
                    Message = $"'{trimmed}' is not a selectable field. Allowed fields: {allowedNames}."
                });
                continue;
            }

            fields.Add(new SelectedField
            {
                PropertyName = property.PropertyName,
                QueryFieldName = property.QueryFieldName
            });
        }

        return new FieldSelectionParseResult
        {
            Fields = fields,
            Errors = errors
        };
    }
}
