using System.Linq.Expressions;
using RestLib.Internal;

namespace RestLib.Filtering;

/// <summary>
/// Represents configuration for a single filterable property.
/// </summary>
public class FilterPropertyConfiguration
{
    /// <summary>
    /// Gets the original property name in C# (e.g., "CategoryId").
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the snake_case parameter name for the query string (e.g., "category_id").
    /// </summary>
    public required string QueryParameterName { get; init; }

    /// <summary>
    /// Gets the property type for value conversion.
    /// </summary>
    public required Type PropertyType { get; init; }

    /// <summary>
    /// Gets the set of allowed filter operators for this property.
    /// Defaults to <see cref="FilterOperator.Eq"/> only when not explicitly configured.
    /// </summary>
    public required IReadOnlySet<FilterOperator> AllowedOperators { get; init; }
}

/// <summary>
/// Holds the filter configuration for an entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class FilterConfiguration<TEntity>
    where TEntity : class
{
    /// <summary>
    /// The default operator set when none is specified: equality only.
    /// </summary>
    private static readonly HashSet<FilterOperator> DefaultOperators = [FilterOperator.Eq];

    private readonly List<FilterPropertyConfiguration> _properties = [];

    /// <summary>
    /// Gets the configured filter properties.
    /// </summary>
    public IReadOnlyList<FilterPropertyConfiguration> Properties => _properties;

    /// <summary>
    /// Adds a filterable property using a property expression with the default operator (eq only).
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="propertyExpression">Expression selecting the property.</param>
    public void AddProperty<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression)
    {
        AddProperty(propertyExpression, []);
    }

    /// <summary>
    /// Adds a filterable property using a property expression with explicit operators.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="propertyExpression">Expression selecting the property.</param>
    /// <param name="allowedOperators">
    /// The operators to allow. When empty, only <see cref="FilterOperator.Eq"/> is allowed.
    /// </param>
    public void AddProperty<TProperty>(
        Expression<Func<TEntity, TProperty>> propertyExpression,
        params IReadOnlyList<FilterOperator> allowedOperators)
    {
        var memberExpression = propertyExpression.Body as MemberExpression
            ?? throw new ArgumentException("Expression must be a member expression", nameof(propertyExpression));

        var propertyName = memberExpression.Member.Name;
        var queryParamName = NamingUtils.ConvertToSnakeCase(propertyName);

        AddPropertyInternal(propertyName, queryParamName, typeof(TProperty), allowedOperators);
    }

    /// <summary>
    /// Adds a filterable property using explicit parameters with the default operator (eq only).
    /// </summary>
    /// <param name="propertyName">The C# property name.</param>
    /// <param name="queryParameterName">The snake_case query parameter name.</param>
    /// <param name="propertyType">The property type.</param>
    internal void AddProperty(string propertyName, string queryParameterName, Type propertyType)
    {
        AddPropertyInternal(propertyName, queryParameterName, propertyType, []);
    }

    /// <summary>
    /// Adds a filterable property using explicit parameters with explicit operators.
    /// </summary>
    /// <param name="propertyName">The C# property name.</param>
    /// <param name="queryParameterName">The snake_case query parameter name.</param>
    /// <param name="propertyType">The property type.</param>
    /// <param name="allowedOperators">
    /// The operators to allow. When empty, only <see cref="FilterOperator.Eq"/> is allowed.
    /// </param>
    internal void AddProperty(string propertyName, string queryParameterName, Type propertyType, IReadOnlyList<FilterOperator> allowedOperators)
    {
        AddPropertyInternal(propertyName, queryParameterName, propertyType, allowedOperators);
    }

    private void AddPropertyInternal(string propertyName, string queryParameterName, Type propertyType, IReadOnlyList<FilterOperator> allowedOperators)
    {
        if (_properties.Any(p => string.Equals(p.PropertyName, propertyName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' is already configured for filtering.");
        }

        var operators = allowedOperators.Count > 0
            ? new HashSet<FilterOperator>(allowedOperators)
            : new HashSet<FilterOperator>(DefaultOperators);

        // Ensure Eq is always present — it is the implicit default operator
        operators.Add(FilterOperator.Eq);

        _properties.Add(new FilterPropertyConfiguration
        {
            PropertyName = propertyName,
            QueryParameterName = queryParameterName,
            PropertyType = propertyType,
            AllowedOperators = operators,
        });
    }
}
