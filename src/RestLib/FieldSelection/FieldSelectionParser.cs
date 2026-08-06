using RestLib.Internal;

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
        var result = ConfiguredQueryListParser.Parse<
            FieldSelectionPropertyConfiguration,
            SelectedField,
            FieldSelectionValidationError>(
                fieldsValue,
                configuration.Properties,
                static property => property.QueryParameterName,
                static segment => new ConfiguredQueryToken(segment),
                static (_, property) => new ConfiguredQueryItemParseResult<SelectedField, FieldSelectionValidationError>(
                    new SelectedField
                    {
                        PropertyName = property.PropertyName,
                        QueryParameterName = property.QueryParameterName
                    },
                    null),
                static (fieldName, allowedNames) => new FieldSelectionValidationError
                {
                    Field = fieldName,
                    Message = $"'{fieldName}' is not a selectable field. Allowed fields: {allowedNames}."
                },
                static fieldName => new FieldSelectionValidationError
                {
                    Field = fieldName,
                    Message = "Duplicate field."
                });

        return new FieldSelectionParseResult
        {
            Fields = result.Items,
            Errors = result.Errors
        };
    }
}
