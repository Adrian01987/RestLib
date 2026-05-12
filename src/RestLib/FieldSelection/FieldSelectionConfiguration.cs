using System.Linq.Expressions;
using RestLib.Internal;

namespace RestLib.FieldSelection;

/// <summary>
/// Defines how sparse field-selection responses render nested property paths.
/// </summary>
public enum FieldSelectionResponseShape
{
    /// <summary>
    /// Nested selections render as flat dotted keys (for example, <c>customer.email</c>).
    /// This is the default for backward compatibility.
    /// </summary>
    Flat,

    /// <summary>
    /// Nested selections render as rebuilt nested JSON objects (for example,
    /// <c>{"customer":{"email":"..."}}</c>) when the sparse projector path is used.
    /// Dense fallback responses continue to render flat dotted keys.
    /// </summary>
    Nested,
}

/// <summary>
/// Represents configuration for a single selectable field.
/// </summary>
public class FieldSelectionPropertyConfiguration
{
    /// <summary>
    /// Gets the original property name in C# (e.g., "CategoryId").
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the snake_case parameter name for the query string (e.g., "category_id").
    /// </summary>
    public required string QueryParameterName { get; init; }
}

/// <summary>
/// Holds the field selection configuration for an entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class FieldSelectionConfiguration<TEntity> where TEntity : class
{
    private readonly List<FieldSelectionPropertyConfiguration> _properties = [];

    /// <summary>
    /// Gets or sets how sparse field-selection responses render nested property paths.
    /// Defaults to <see cref="FieldSelectionResponseShape.Flat"/> for backward compatibility.
    /// Dense fallback responses continue to use flat dotted output even when
    /// <see cref="FieldSelectionResponseShape.Nested"/> is selected.
    /// </summary>
    public FieldSelectionResponseShape ResponseShape { get; set; } = FieldSelectionResponseShape.Flat;

    /// <summary>
    /// Gets the configured selectable properties.
    /// </summary>
    public IReadOnlyList<FieldSelectionPropertyConfiguration> Properties => _properties;

    /// <summary>
    /// Configures sparse field-selection responses to render nested property paths as
    /// nested JSON objects instead of flat dotted keys.
    /// This opt-in affects only the sparse projection path; dense fallback responses
    /// continue to use flat dotted output.
    /// </summary>
    /// <returns>This configuration instance for chaining.</returns>
    public FieldSelectionConfiguration<TEntity> UseNestedObjectsInResponse()
    {
        ResponseShape = FieldSelectionResponseShape.Nested;
        return this;
    }

    /// <summary>
    /// Finds a configured property by its snake_case query parameter name.
    /// </summary>
    /// <param name="queryParameterName">The snake_case parameter name from the query string.</param>
    /// <returns>The matching configuration, or null.</returns>
    public FieldSelectionPropertyConfiguration? FindByQueryName(string queryParameterName)
    {
        return _properties.FirstOrDefault(p =>
            string.Equals(p.QueryParameterName, queryParameterName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a selectable property using a property expression.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="propertyExpression">Expression selecting the property.</param>
    public void AddProperty<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression)
    {
        var propertyPath = NamingUtils.ResolvePropertyPath(propertyExpression, nameof(propertyExpression));
        var propertyName = propertyPath.ClrPath;
        var queryParameterName = propertyPath.QueryPath;

        if (_properties.Any(p => string.Equals(p.PropertyName, propertyName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' is already configured for field selection.");
        }

        _properties.Add(new FieldSelectionPropertyConfiguration
        {
            PropertyName = propertyName,
            QueryParameterName = queryParameterName
        });
    }

    /// <summary>
    /// Adds a selectable property using explicit parameters.
    /// </summary>
    /// <param name="propertyName">The C# property name.</param>
    /// <param name="queryParameterName">The snake_case query parameter name.</param>
    internal void AddProperty(string propertyName, string queryParameterName)
    {
        if (_properties.Any(p => string.Equals(p.PropertyName, propertyName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' is already configured for field selection.");
        }

        _properties.Add(new FieldSelectionPropertyConfiguration
        {
            PropertyName = propertyName,
            QueryParameterName = queryParameterName
        });
    }
}
