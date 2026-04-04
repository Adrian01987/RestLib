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
}

/// <summary>
/// Holds the filter configuration for an entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class FilterConfiguration<TEntity> where TEntity : class
{
  private readonly List<FilterPropertyConfiguration> _properties = [];

  /// <summary>
  /// Gets the configured filter properties.
  /// </summary>
  public IReadOnlyList<FilterPropertyConfiguration> Properties => _properties;

  /// <summary>
  /// Adds a filterable property using a property expression.
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
          $"Property '{propertyName}' is already configured for filtering.");
    }

    _properties.Add(new FilterPropertyConfiguration
    {
      PropertyName = propertyName,
      QueryParameterName = queryParamName,
      PropertyType = typeof(TProperty)
    });
  }

  /// <summary>
  /// Adds a filterable property using explicit parameters.
  /// </summary>
  /// <param name="propertyName">The C# property name.</param>
  /// <param name="queryParameterName">The snake_case query parameter name.</param>
  /// <param name="propertyType">The property type.</param>
  internal void AddProperty(string propertyName, string queryParameterName, Type propertyType)
  {
    if (_properties.Any(p => string.Equals(p.PropertyName, propertyName, StringComparison.Ordinal)))
    {
      throw new InvalidOperationException(
          $"Property '{propertyName}' is already configured for filtering.");
    }

    _properties.Add(new FilterPropertyConfiguration
    {
      PropertyName = propertyName,
      QueryParameterName = queryParameterName,
      PropertyType = propertyType
    });
  }
}
