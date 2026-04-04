using System.Linq.Expressions;
using RestLib.Batch;
using RestLib.FieldSelection;
using RestLib.Filtering;
using RestLib.Hooks;
using RestLib.Internal;
using RestLib.Sorting;

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
  private readonly SortConfiguration<TEntity> _sortConfiguration = new();
  private readonly FieldSelectionConfiguration<TEntity> _fieldSelectionConfiguration = new();
  private readonly HashSet<BatchAction> _enabledBatchActions = [];
  private readonly Dictionary<RestLibOperation, string> _rateLimitPolicies = [];
  private readonly HashSet<RestLibOperation> _disabledRateLimitOperations = [];
  private readonly RestLibOpenApiConfiguration _openApi = new();
  private string? _defaultRateLimitPolicy;
  private RestLibHooks<TEntity, TKey>? _hooks;
  private HashSet<RestLibOperation>? _includedOperations;
  private HashSet<RestLibOperation>? _excludedOperations;

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
  /// Gets whether any filters have been configured.
  /// </summary>
  internal bool HasFilters => _filterConfiguration.Properties.Count > 0;

  /// <summary>
  /// Gets the sort configuration for this entity.
  /// </summary>
  internal SortConfiguration<TEntity> SortConfiguration => _sortConfiguration;

  /// <summary>
  /// Gets whether any sortable properties have been configured.
  /// </summary>
  internal bool HasSorting => _sortConfiguration.Properties.Count > 0;

  /// <summary>
  /// Gets the field selection configuration for this entity.
  /// </summary>
  internal FieldSelectionConfiguration<TEntity> FieldSelectionConfiguration => _fieldSelectionConfiguration;

  /// <summary>
  /// Gets whether any selectable fields have been configured.
  /// </summary>
  internal bool HasFieldSelection => _fieldSelectionConfiguration.Properties.Count > 0;

  /// <summary>
  /// Gets the set of enabled batch actions.
  /// </summary>
  internal IReadOnlySet<BatchAction> EnabledBatchActions => _enabledBatchActions;

  /// <summary>
  /// Gets a value indicating whether any batch actions have been enabled.
  /// </summary>
  internal bool HasBatch => _enabledBatchActions.Count > 0;

  /// <summary>
  /// Gets the configured hooks for the request processing pipeline.
  /// </summary>
  internal RestLibHooks<TEntity, TKey>? Hooks => _hooks;

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
      var memberExpression = NamingUtils.GetMemberExpression(expression.Body, nameof(propertyExpressions));

      var propertyName = memberExpression.Member.Name;
      var propertyType = memberExpression.Type;
      var queryParamName = NamingUtils.ConvertToSnakeCase(propertyName);

      _filterConfiguration.AddProperty(propertyName, queryParamName, propertyType);
    }
    return this;
  }

  /// <summary>
  /// Configures which properties can be filtered on via query parameters.
  /// Property names are automatically converted to snake_case in the query string.
  /// </summary>
  /// <param name="propertyNames">The entity property names to allow for filtering.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.AllowFiltering("CategoryId", "IsActive");
  /// // Results in query parameters: ?category_id=5&amp;is_active=true
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> AllowFiltering(
      params string[] propertyNames)
  {
    foreach (var propertyName in propertyNames)
    {
      var property = NamingUtils.ResolveProperty<TEntity>(propertyName, nameof(propertyNames));

      var queryParamName = NamingUtils.ConvertToSnakeCase(property.Name);
      _filterConfiguration.AddProperty(property.Name, queryParamName, property.PropertyType);
    }

    return this;
  }

  /// <summary>
  /// Configures a single property for filtering with specific allowed operators.
  /// The property name is automatically converted to snake_case in the query string.
  /// </summary>
  /// <param name="propertyExpression">Expression selecting the filterable property.</param>
  /// <param name="operators">
  /// The operators to allow for this property.
  /// <see cref="Filtering.FilterOperator.Eq"/> is always implicitly included.
  /// Use <see cref="Filtering.FilterOperators"/> presets for common operator sets.
  /// </param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.AllowFiltering(p => p.Price, FilterOperators.Comparison);
  /// // Allows: ?price=10, ?price[gte]=10, ?price[lte]=100, etc.
  ///
  /// config.AllowFiltering(p => p.Name, FilterOperators.String);
  /// // Allows: ?name=Widget, ?name[contains]=wid, ?name[starts_with]=Wid
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> AllowFiltering(
      Expression<Func<TEntity, object?>> propertyExpression,
      params FilterOperator[] operators)
  {
    var memberExpression = NamingUtils.GetMemberExpression(propertyExpression.Body, nameof(propertyExpression));

    var propertyName = memberExpression.Member.Name;
    var propertyType = memberExpression.Type;
    var queryParamName = NamingUtils.ConvertToSnakeCase(propertyName);

    _filterConfiguration.AddProperty(propertyName, queryParamName, propertyType, operators);
    return this;
  }

  /// <summary>
  /// Configures a single property for filtering with specific allowed operators (string-based).
  /// The property name is automatically converted to snake_case in the query string.
  /// </summary>
  /// <param name="propertyName">The entity property name to allow for filtering.</param>
  /// <param name="operators">
  /// The operators to allow for this property.
  /// <see cref="Filtering.FilterOperator.Eq"/> is always implicitly included.
  /// Use <see cref="Filtering.FilterOperators"/> presets for common operator sets.
  /// </param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.AllowFiltering("Price", FilterOperators.Comparison);
  /// config.AllowFiltering("Name", FilterOperators.String);
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> AllowFiltering(
      string propertyName,
      params FilterOperator[] operators)
  {
    var property = NamingUtils.ResolveProperty<TEntity>(propertyName, nameof(propertyName));

    var queryParamName = NamingUtils.ConvertToSnakeCase(property.Name);
    _filterConfiguration.AddProperty(property.Name, queryParamName, property.PropertyType, operators);
    return this;
  }

  /// <summary>
  /// Configures which properties can be sorted on via the sort query parameter.
  /// Property names are automatically converted to snake_case in the query string.
  /// </summary>
  /// <param name="propertyExpressions">Expressions selecting the sortable properties.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.AllowSorting(p => p.Price, p => p.Name);
  /// // Request: ?sort=price:asc,name:desc
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> AllowSorting(
      params Expression<Func<TEntity, object?>>[] propertyExpressions)
  {
    foreach (var expression in propertyExpressions)
    {
      var memberExpression = NamingUtils.GetMemberExpression(expression.Body, nameof(propertyExpressions));

      var propertyName = memberExpression.Member.Name;
      var propertyType = memberExpression.Type;
      var queryParamName = NamingUtils.ConvertToSnakeCase(propertyName);

      _sortConfiguration.AddProperty(propertyName, queryParamName, propertyType);
    }
    return this;
  }

  /// <summary>
  /// Configures which properties can be sorted on via the sort query parameter.
  /// Property names are automatically converted to snake_case in the query string.
  /// </summary>
  /// <param name="propertyNames">The entity property names to allow for sorting.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.AllowSorting("Price", "Name");
  /// // Request: ?sort=price:asc,name:desc
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> AllowSorting(
      params string[] propertyNames)
  {
    foreach (var propertyName in propertyNames)
    {
      var property = NamingUtils.ResolveProperty<TEntity>(propertyName, nameof(propertyNames));

      var queryParamName = NamingUtils.ConvertToSnakeCase(property.Name);
      _sortConfiguration.AddProperty(property.Name, queryParamName, property.PropertyType);
    }
    return this;
  }

  /// <summary>
  /// Sets the default sort expression used when the client does not send a sort parameter.
  /// Must be called after <see cref="AllowSorting(Expression{Func{TEntity, object?}}[])"/>
  /// so properties are already registered.
  /// </summary>
  /// <param name="sortExpression">
  /// Comma-separated sort expression, e.g. "name:asc" or "price:desc,name:asc".
  /// </param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.AllowSorting(p => p.Price, p => p.Name);
  /// config.DefaultSort("name:asc");
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> DefaultSort(string sortExpression)
  {
    if (_sortConfiguration.Properties.Count == 0)
      throw new InvalidOperationException(
          "DefaultSort must be called after AllowSorting. No sortable properties are configured.");

    var result = SortParser.Parse(sortExpression, _sortConfiguration);
    if (!result.IsValid)
    {
      var errorMessages = string.Join("; ", result.Errors.Select(e => $"{e.Field}: {e.Message}"));
      throw new ArgumentException(
          $"Invalid default sort expression '{sortExpression}': {errorMessages}",
          nameof(sortExpression));
    }

    _sortConfiguration.SetDefaultSort(result.Fields);
    return this;
  }

  /// <summary>
  /// Configures which properties can be selected via the <c>fields</c> query parameter.
  /// Property names are automatically converted to snake_case in the query string.
  /// </summary>
  /// <param name="propertyExpressions">Expressions selecting the selectable properties.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.AllowFieldSelection(p => p.Id, p => p.Name, p => p.Price);
  /// // Request: ?fields=id,name,price
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> AllowFieldSelection(
      params Expression<Func<TEntity, object?>>[] propertyExpressions)
  {
    foreach (var expression in propertyExpressions)
    {
      var memberExpression = NamingUtils.GetMemberExpression(expression.Body, nameof(propertyExpressions));

      var propertyName = memberExpression.Member.Name;
      var queryFieldName = NamingUtils.ConvertToSnakeCase(propertyName);

      _fieldSelectionConfiguration.AddProperty(propertyName, queryFieldName);
    }
    return this;
  }

  /// <summary>
  /// Configures which properties can be selected via the <c>fields</c> query parameter.
  /// Property names are automatically converted to snake_case in the query string.
  /// </summary>
  /// <param name="propertyNames">The entity property names to allow for field selection.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.AllowFieldSelection("Id", "Name", "Price");
  /// // Request: ?fields=id,name,price
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> AllowFieldSelection(
      params string[] propertyNames)
  {
    foreach (var propertyName in propertyNames)
    {
      var property = NamingUtils.ResolveProperty<TEntity>(propertyName, nameof(propertyNames));

      var queryFieldName = NamingUtils.ConvertToSnakeCase(property.Name);
      _fieldSelectionConfiguration.AddProperty(property.Name, queryFieldName);
    }
    return this;
  }

  /// <summary>
  /// Enables batch operations for this resource.
  /// When called with no arguments, all batch actions (Create, Update, Patch, Delete) are enabled.
  /// </summary>
  /// <param name="actions">
  /// The batch actions to enable. If empty, all actions are enabled.
  /// </param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// // Enable all batch actions
  /// config.EnableBatch();
  ///
  /// // Enable specific actions
  /// config.EnableBatch(BatchAction.Create, BatchAction.Delete);
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> EnableBatch(
      params BatchAction[] actions)
  {
    if (actions.Length == 0)
    {
      foreach (var action in Enum.GetValues<BatchAction>())
      {
        _enabledBatchActions.Add(action);
      }
    }
    else
    {
      foreach (var action in actions)
      {
        _enabledBatchActions.Add(action);
      }
    }

    return this;
  }

  /// <summary>
  /// Applies a rate limiting policy to all operations on this resource.
  /// Per-operation overrides and <see cref="DisableRateLimiting"/> take precedence.
  /// </summary>
  /// <param name="policyName">The name of the rate limiting policy defined via <c>AddRateLimiter</c>.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.UseRateLimiting("my-policy");
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> UseRateLimiting(string policyName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
    _defaultRateLimitPolicy = policyName;
    return this;
  }

  /// <summary>
  /// Applies a rate limiting policy to specific operations on this resource.
  /// Takes precedence over the default policy set via <see cref="UseRateLimiting(string)"/>.
  /// </summary>
  /// <param name="policyName">The name of the rate limiting policy defined via <c>AddRateLimiter</c>.</param>
  /// <param name="operations">The operations to apply the policy to.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.UseRateLimiting("read-policy", RestLibOperation.GetAll, RestLibOperation.GetById);
  /// config.UseRateLimiting("write-policy", RestLibOperation.Create, RestLibOperation.Update);
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> UseRateLimiting(
      string policyName,
      params RestLibOperation[] operations)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
    foreach (var operation in operations)
    {
      _rateLimitPolicies[operation] = policyName;
    }
    return this;
  }

  /// <summary>
  /// Disables rate limiting for specific operations on this resource.
  /// This is useful when a global rate limiter is applied via middleware
  /// but certain RestLib operations should be exempt.
  /// Takes precedence over both per-operation and default policies.
  /// </summary>
  /// <param name="operations">The operations to exempt from rate limiting.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.UseRateLimiting("strict-policy");
  /// config.DisableRateLimiting(RestLibOperation.GetById);
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> DisableRateLimiting(
      params RestLibOperation[] operations)
  {
    foreach (var operation in operations)
    {
      _disabledRateLimitOperations.Add(operation);
    }
    return this;
  }

  /// <summary>
  /// Includes the specified operations. All others will be excluded unless also included
  /// in a subsequent call. Multiple calls are merged (unioned).
  /// Cannot be combined with <see cref="ExcludeOperations"/>.
  /// </summary>
  /// <param name="operations">The operations to include.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
  /// // Later, add Create as well:
  /// config.IncludeOperations(RestLibOperation.Create);
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> IncludeOperations(
      params RestLibOperation[] operations)
  {
    if (_excludedOperations is not null)
      throw new InvalidOperationException(
          "Cannot use IncludeOperations when ExcludeOperations has already been called.");

    if (_includedOperations is null)
      _includedOperations = [.. operations];
    else
      _includedOperations.UnionWith(operations);

    return this;
  }

  /// <summary>
  /// Excludes the specified operations. All others will be included.
  /// Cannot be combined with <see cref="IncludeOperations"/>.
  /// </summary>
  /// <param name="operations">The operations to exclude.</param>
  /// <returns>This configuration instance for chaining.</returns>
  /// <example>
  /// <code>
  /// config.ExcludeOperations(RestLibOperation.Delete, RestLibOperation.Patch);
  /// </code>
  /// </example>
  public RestLibEndpointConfiguration<TEntity, TKey> ExcludeOperations(
      params RestLibOperation[] operations)
  {
    if (_includedOperations is not null)
      throw new InvalidOperationException(
          "Cannot use ExcludeOperations when IncludeOperations has already been called.");

    _excludedOperations = [.. operations];
    return this;
  }

  /// <summary>
  /// Determines whether the specified operation should be mapped.
  /// </summary>
  /// <param name="operation">The operation to check.</param>
  /// <returns><c>true</c> if the operation is enabled; otherwise, <c>false</c>.</returns>
  public bool IsOperationEnabled(RestLibOperation operation)
  {
    if (_includedOperations is not null)
      return _includedOperations.Contains(operation);

    if (_excludedOperations is not null)
      return !_excludedOperations.Contains(operation);

    return true; // all enabled by default
  }

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
  /// Checks if an operation allows anonymous access.
  /// </summary>
  internal bool IsAnonymous(RestLibOperation operation) =>
    _anonymousOperations.Contains(operation);

  /// <summary>
  /// Gets the policies required for an operation.
  /// </summary>
  internal string[]? GetPolicies(RestLibOperation operation) =>
      _operationPolicies.TryGetValue(operation, out var policies) ? policies : null;

  /// <summary>
  /// Checks whether a specific batch action is enabled.
  /// </summary>
  /// <param name="action">The batch action to check.</param>
  /// <returns><c>true</c> if the action is enabled; otherwise, <c>false</c>.</returns>
  internal bool IsBatchActionEnabled(BatchAction action) =>
    _enabledBatchActions.Contains(action);

  /// <summary>
  /// Gets whether rate limiting is explicitly disabled for an operation.
  /// </summary>
  internal bool IsRateLimitingDisabled(RestLibOperation operation) =>
      _disabledRateLimitOperations.Contains(operation);

  /// <summary>
  /// Resolves the rate limit policy for an operation.
  /// Returns null if no policy applies (per-operation first, then default).
  /// </summary>
  internal string? GetRateLimitPolicy(RestLibOperation operation) =>
      _rateLimitPolicies.TryGetValue(operation, out var policy) ? policy : _defaultRateLimitPolicy;
}
