using System.Linq.Expressions;
using RestLib.Internal;

namespace RestLib.FieldSelection;

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
    /// Gets the configured selectable properties.
    /// </summary>
    public IReadOnlyList<FieldSelectionPropertyConfiguration> Properties => _properties;

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
        var memberExpression = propertyExpression.Body as MemberExpression
            ?? throw new ArgumentException("Expression must be a member expression", nameof(propertyExpression));

        var propertyName = memberExpression.Member.Name;
        var queryParameterName = NamingUtils.ConvertToSnakeCase(propertyName);

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
