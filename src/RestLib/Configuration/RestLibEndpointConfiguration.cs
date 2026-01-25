using System.Linq.Expressions;
using RestLib.Filtering;
using RestLib.Hooks;

namespace RestLib.Configuration;

/// <summary>
/// Configuration options for RestLib endpoints.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
public class RestLibEndpointConfiguration<TEntity, TKey>
    where TEntity : class
{
  private readonly HashSet<RestLibOperation> _anonymousOperations = [];
  private readonly Dictionary<RestLibOperation, string[]> _operationPolicies = [];
  private readonly FilterConfiguration<TEntity> _filterConfiguration = new();
  private RestLibHooks<TEntity, TKey>? _hooks;
  private readonly RestLibOpenApiConfiguration _openApi = new();

  /// <summary>
  /// Gets or sets the function to extract the key from an entity.
  /// If not set, the system will attempt to find an 'Id' property via reflection.
  /// </summary>
  public Func<TEntity, TKey>? KeySelector { get; set; }

  /// <summary>
  /// Gets the OpenAPI metadata configuration for this resource.
  /// </summary>
  /// <example>
  /// <code>
  /// config.OpenApi.Tag = "Products";
  /// config.OpenApi.Deprecated = true;
  /// config.OpenApi.Summaries.GetAll = "List all products";
  /// </code>
  /// </example>
  public RestLibOpenApiConfiguration OpenApi => _openApi;

  /// <summary>
  /// Gets the filter configuration for this entity.
  /// </summary>
  internal FilterConfiguration<TEntity> FilterConfiguration => _filterConfiguration;

  /// <summary>
  /// Marks all operations as allowing anonymous access.
  /// </summary>
  public RestLibEndpointConfiguration<TEntity, TKey> AllowAnonymous()
  {
    foreach (var operation in Enum.GetValues<RestLibOperation>())
    {
      _anonymousOperations.Add(operation);
    }
    return this;
  }

  /// <summary>
  /// Marks specific operations as allowing anonymous access.
  /// </summary>
  /// <param name="operations">The operations to allow anonymous access.</param>
  public RestLibEndpointConfiguration<TEntity, TKey> AllowAnonymous(params RestLibOperation[] operations)
  {
    foreach (var operation in operations)
    {
      _anonymousOperations.Add(operation);
    }
    return this;
  }

  /// <summary>
  /// Requires a specific authorization policy for an operation.
  /// </summary>
  /// <param name="operation">The operation to protect.</param>
  /// <param name="policyNames">The policy names to require.</param>
  public RestLibEndpointConfiguration<TEntity, TKey> RequirePolicy(
      RestLibOperation operation,
      params string[] policyNames)
  {
    _operationPolicies[operation] = policyNames;
    return this;
  }

  /// <summary>
  /// Requires specific authorization policies for multiple operations.
  /// </summary>
  /// <param name="policyName">The policy name to require.</param>
  /// <param name="operations">The operations to protect with this policy.</param>
  public RestLibEndpointConfiguration<TEntity, TKey> RequirePolicyForOperations(
      string policyName,
      params RestLibOperation[] operations)
  {
    foreach (var operation in operations)
    {
      _operationPolicies[operation] = [policyName];
    }
    return this;
  }

  /// <summary>
  /// Configures which properties can be filtered on via query parameters.
  /// Property names are automatically converted to snake_case in the query string.
  /// </summary>
  /// <param name="propertyExpressions">Expressions selecting the filterable properties.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.AllowFiltering(p => p.CategoryId, p => p.IsActive);
  /// // Results in query parameters: ?category_id=5&amp;is_active=true
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> AllowFiltering(
      params Expression<Func<TEntity, object?>>[] propertyExpressions)
  {
    foreach (var expression in propertyExpressions)
    {
      // Handle cases where the expression is wrapped in Convert (for value types)
      var memberExpression = expression.Body as MemberExpression
          ?? (expression.Body as UnaryExpression)?.Operand as MemberExpression;

      if (memberExpression == null)
      {
        throw new ArgumentException(
            "Each expression must be a property access expression (e.g., p => p.PropertyName)",
            nameof(propertyExpressions));
      }

      var propertyName = memberExpression.Member.Name;
      var propertyType = memberExpression.Type;
      var queryParamName = FilterConfiguration<TEntity>.ConvertToSnakeCase(propertyName);

      _filterConfiguration.AddProperty(propertyName, queryParamName, propertyType);
    }
    return this;
  }

  /// <summary>
  /// Checks if an operation allows anonymous access.
  /// </summary>
  internal bool IsAnonymous(RestLibOperation operation) => _anonymousOperations.Contains(operation);

  /// <summary>
  /// Gets the policies required for an operation.
  /// </summary>
  internal string[]? GetPolicies(RestLibOperation operation) =>
      _operationPolicies.TryGetValue(operation, out var policies) ? policies : null;

  /// <summary>
  /// Gets whether any filters have been configured.
  /// </summary>
  internal bool HasFilters => _filterConfiguration.Properties.Count > 0;

  /// <summary>
  /// Configures hooks for the request processing pipeline.
  /// </summary>
  /// <param name="configure">An action to configure the hooks.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.UseHooks(hooks =>
  /// {
  ///     hooks.OnRequestReceived = async ctx => { /* log request */ };
  ///     hooks.BeforePersist = async ctx => { ctx.Entity!.CreatedAt = DateTime.UtcNow; };
  ///     hooks.OnError = async ctx => { /* handle error */ ctx.Handled = true; };
  /// });
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> UseHooks(
      Action<RestLibHooks<TEntity, TKey>> configure)
  {
    _hooks ??= new RestLibHooks<TEntity, TKey>();
    configure(_hooks);
    return this;
  }

  /// <summary>
  /// Gets the configured hooks for the request processing pipeline.
  /// </summary>
  internal RestLibHooks<TEntity, TKey>? Hooks => _hooks;
}
