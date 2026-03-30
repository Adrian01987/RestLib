using System.Linq.Expressions;
using System.Text.Json;

namespace RestLib.FieldSelection;

/// <summary>
/// Represents configuration for a single selectable field.
/// </summary>
public class FieldPropertyConfiguration
{
    /// <summary>
    /// Gets the original property name in C# (e.g., "CategoryId").
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the snake_case field name for the query string (e.g., "category_id").
    /// </summary>
    public required string QueryFieldName { get; init; }
}

/// <summary>
/// Holds the field selection configuration for an entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class FieldSelectionConfiguration<TEntity> where TEntity : class
{
    private readonly List<FieldPropertyConfiguration> _properties = [];

    /// <summary>
    /// Gets the configured selectable properties.
    /// </summary>
    public IReadOnlyList<FieldPropertyConfiguration> Properties => _properties;

    /// <summary>
    /// Finds a configured property by its snake_case query field name.
    /// </summary>
    /// <param name="queryFieldName">The snake_case field name from the query string.</param>
    /// <returns>The matching configuration, or null.</returns>
    public FieldPropertyConfiguration? FindByQueryName(string queryFieldName)
    {
        return _properties.FirstOrDefault(p =>
            string.Equals(p.QueryFieldName, queryFieldName, StringComparison.OrdinalIgnoreCase));
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
        var queryFieldName = ConvertToSnakeCase(propertyName);

        _properties.Add(new FieldPropertyConfiguration
        {
            PropertyName = propertyName,
            QueryFieldName = queryFieldName
        });
    }

    /// <summary>
    /// Converts a PascalCase property name to snake_case.
    /// </summary>
    /// <param name="propertyName">The PascalCase property name.</param>
    /// <returns>The snake_case equivalent.</returns>
    internal static string ConvertToSnakeCase(string propertyName)
    {
        return JsonNamingPolicy.SnakeCaseLower.ConvertName(propertyName);
    }

    /// <summary>
    /// Adds a selectable property using explicit parameters.
    /// </summary>
    /// <param name="propertyName">The C# property name.</param>
    /// <param name="queryFieldName">The snake_case query field name.</param>
    internal void AddProperty(string propertyName, string queryFieldName)
    {
        _properties.Add(new FieldPropertyConfiguration
        {
            PropertyName = propertyName,
            QueryFieldName = queryFieldName
        });
    }
}
