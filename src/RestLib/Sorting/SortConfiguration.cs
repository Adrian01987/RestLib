using System.Linq.Expressions;
using RestLib.Internal;

namespace RestLib.Sorting;

/// <summary>
/// Represents configuration for a single sortable property.
/// </summary>
public class SortPropertyConfiguration
{
    /// <summary>
    /// Gets the original property name in C# (e.g., "Price").
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the snake_case parameter name for the query string (e.g., "price").
    /// </summary>
    public required string QueryParameterName { get; init; }

    /// <summary>
    /// Gets the property type.
    /// </summary>
    public required Type PropertyType { get; init; }
}

/// <summary>
/// Holds the sort configuration for an entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class SortConfiguration<TEntity> where TEntity : class
{
    private readonly List<SortPropertyConfiguration> _properties = [];

    /// <summary>
    /// Gets the configured sortable properties.
    /// </summary>
    public IReadOnlyList<SortPropertyConfiguration> Properties => _properties;

    /// <summary>
    /// Gets the default sort fields applied when the client does not provide a sort parameter.
    /// </summary>
    public IReadOnlyList<SortField>? DefaultSortFields { get; private set; }

    /// <summary>
    /// Adds a sortable property using a property expression.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="propertyExpression">Expression selecting the property.</param>
    public void AddProperty<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression)
    {
        var memberExpression = propertyExpression.Body as MemberExpression
            ?? throw new ArgumentException("Expression must be a member expression", nameof(propertyExpression));

        var propertyName = memberExpression.Member.Name;
        var queryParamName = NamingUtils.ConvertToSnakeCase(propertyName);

        if (_properties.Any(p => string.Equals(p.PropertyName, propertyName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' is already configured for sorting.");
        }

        _properties.Add(new SortPropertyConfiguration
        {
            PropertyName = propertyName,
            QueryParameterName = queryParamName,
            PropertyType = typeof(TProperty)
        });
    }

    /// <summary>
    /// Sets the default sort fields applied when no sort parameter is provided.
    /// </summary>
    /// <param name="fields">The default sort fields.</param>
    public void SetDefaultSort(IReadOnlyList<SortField> fields)
    {
        DefaultSortFields = fields;
    }

    /// <summary>
    /// Finds a sortable property by its query parameter name (case-insensitive).
    /// </summary>
    /// <param name="queryParameterName">The query parameter name to look up.</param>
    /// <returns>The property configuration, or null if not found.</returns>
    public SortPropertyConfiguration? FindByQueryName(string queryParameterName)
    {
        return _properties.FirstOrDefault(p =>
            string.Equals(p.QueryParameterName, queryParameterName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds a sortable property using explicit parameters.
    /// </summary>
    /// <param name="propertyName">The C# property name.</param>
    /// <param name="queryParameterName">The snake_case query parameter name.</param>
    /// <param name="propertyType">The property type.</param>
    internal void AddProperty(string propertyName, string queryParameterName, Type propertyType)
    {
        if (_properties.Any(p => string.Equals(p.PropertyName, propertyName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' is already configured for sorting.");
        }

        _properties.Add(new SortPropertyConfiguration
        {
            PropertyName = propertyName,
            QueryParameterName = queryParameterName,
            PropertyType = propertyType
        });
    }
}
