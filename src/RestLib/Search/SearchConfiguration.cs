using System.Linq.Expressions;
using RestLib.Internal;

namespace RestLib.Search;

/// <summary>
/// Represents configuration for a single searchable property.
/// </summary>
public class SearchPropertyConfiguration
{
    /// <summary>
    /// Gets the CLR property path (for example, <c>ProductName</c> or <c>Customer.Email</c>).
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    /// Gets the snake_case query-style property path (for example, <c>product_name</c> or <c>customer.email</c>).
    /// </summary>
    public required string QueryParameterName { get; init; }
}

/// <summary>
/// Configures search options for a resource.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class RestLibSearchOptions<TEntity>
    where TEntity : class
{
    /// <summary>
    /// The default query parameter name used for search.
    /// </summary>
    internal const string DefaultQueryParameterName = "q";

    /// <summary>
    /// Gets or sets the query parameter name used for search.
    /// Defaults to <c>q</c>.
    /// </summary>
    public string QueryParameterName { get; set; } = DefaultQueryParameterName;

    /// <summary>
    /// Gets or sets a value indicating whether search uses case-sensitive matching.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool CaseSensitive { get; set; }
}

/// <summary>
/// Holds search configuration for an entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
internal sealed class SearchConfiguration<TEntity>
    where TEntity : class
{
    private readonly List<SearchPropertyConfiguration> _properties = [];

    /// <summary>
    /// Gets the configured searchable properties.
    /// </summary>
    internal IReadOnlyList<SearchPropertyConfiguration> Properties => _properties;

    /// <summary>
    /// Gets the query parameter name used for search.
    /// </summary>
    internal string QueryParameterName { get; private set; } = RestLibSearchOptions<TEntity>.DefaultQueryParameterName;

    /// <summary>
    /// Gets a value indicating whether search uses case-sensitive matching.
    /// </summary>
    internal bool CaseSensitive { get; private set; }

    /// <summary>
    /// Applies configured search options.
    /// </summary>
    /// <param name="options">The search options to apply.</param>
    internal void ApplyOptions(RestLibSearchOptions<TEntity> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ArgumentException.ThrowIfNullOrWhiteSpace(options.QueryParameterName);
        QueryParameterName = options.QueryParameterName.Trim();
        CaseSensitive = options.CaseSensitive;
    }

    /// <summary>
    /// Adds a searchable property using a property expression.
    /// </summary>
    /// <param name="propertyExpression">Expression selecting the searchable property.</param>
    internal void AddProperty(Expression<Func<TEntity, string?>> propertyExpression)
    {
        var propertyPath = NamingUtils.ResolvePropertyPath(propertyExpression, nameof(propertyExpression));
        AddProperty(propertyPath);
    }

    /// <summary>
    /// Adds a searchable property using a CLR or query-style property path.
    /// </summary>
    /// <param name="propertyName">The searchable property path.</param>
    internal void AddProperty(string propertyName)
    {
        var propertyPath = NamingUtils.ResolvePropertyPath<TEntity>(propertyName, nameof(propertyName));
        AddProperty(propertyPath);
    }

    private void AddProperty(PropertyPath propertyPath)
    {
        ValidateStringProperty(propertyPath);

        if (_properties.Any(p => string.Equals(p.PropertyName, propertyPath.ClrPath, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Property '{propertyPath.ClrPath}' is already configured for search.");
        }

        _properties.Add(new SearchPropertyConfiguration
        {
            PropertyName = propertyPath.ClrPath,
            QueryParameterName = propertyPath.QueryPath
        });
    }

    private void ValidateStringProperty(PropertyPath propertyPath)
    {
        if (propertyPath.LeafPropertyType != typeof(string))
        {
            throw new ArgumentException(
                $"Property path '{propertyPath.ClrPath}' must resolve to a string property to be used for search, but resolved to '{propertyPath.LeafPropertyType.Name}'.",
                nameof(propertyPath));
        }
    }
}
