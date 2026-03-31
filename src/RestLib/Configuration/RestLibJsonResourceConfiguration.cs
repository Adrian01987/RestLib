using RestLib.Batch;

namespace RestLib.Configuration;

/// <summary>
/// JSON-backed configuration for a single RestLib resource.
/// </summary>
public class RestLibJsonResourceConfiguration
{
  /// <summary>
  /// Gets or sets the unique resource name used during registration.
  /// </summary>
  public required string Name { get; set; }

  /// <summary>
  /// Gets or sets the route prefix for the resource.
  /// </summary>
  public required string Route { get; set; }

  /// <summary>
  /// Gets or sets the property name used as the entity key.
  /// Defaults to <c>Id</c> when omitted.
  /// </summary>
  public string? KeyProperty { get; set; }

  /// <summary>
  /// Gets or sets the operations that allow anonymous access.
  /// </summary>
  public List<RestLibOperation> AllowAnonymous { get; set; } = [];

  /// <summary>
  /// Gets or sets whether all operations allow anonymous access.
  /// </summary>
  public bool AllowAnonymousAll { get; set; }

  /// <summary>
  /// Gets or sets the operation inclusion/exclusion rules.
  /// </summary>
  public RestLibJsonOperationSelection? Operations { get; set; }

  /// <summary>
  /// Gets or sets the policy requirements for specific operations.
  /// </summary>
  public Dictionary<string, string[]> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// Gets or sets the filterable entity property names.
  /// </summary>
  public List<string> Filtering { get; set; } = [];

  /// <summary>
  /// Gets or sets the sortable entity property names.
  /// </summary>
  public List<string> Sorting { get; set; } = [];

  /// <summary>
  /// Gets or sets the default sort expression (e.g. "name:asc,price:desc").
  /// </summary>
  public string? DefaultSort { get; set; }

  /// <summary>
  /// Gets or sets the selectable entity property names for sparse fieldsets.
  /// </summary>
  public List<string> FieldSelection { get; set; } = [];

  /// <summary>
  /// Gets or sets the batch operations configuration for this resource.
  /// </summary>
  public RestLibJsonBatchConfiguration? Batch { get; set; }

  /// <summary>
  /// Gets or sets the rate limiting configuration for this resource.
  /// </summary>
  public RestLibJsonRateLimitingConfiguration? RateLimiting { get; set; }

  /// <summary>
  /// Gets or sets the OpenAPI metadata configuration.
  /// </summary>
  public RestLibJsonOpenApiConfiguration? OpenApi { get; set; }

  /// <summary>
  /// Gets or sets the named hook configuration.
  /// </summary>
  public RestLibJsonHookConfiguration? Hooks { get; set; }
}

/// <summary>
/// JSON configuration for selecting enabled operations.
/// </summary>
public class RestLibJsonOperationSelection
{
  /// <summary>
  /// Gets or sets the explicitly included operations.
  /// </summary>
  public List<RestLibOperation> Include { get; set; } = [];

  /// <summary>
  /// Gets or sets the explicitly excluded operations.
  /// </summary>
  public List<RestLibOperation> Exclude { get; set; } = [];
}

/// <summary>
/// JSON configuration for OpenAPI metadata.
/// </summary>
public class RestLibJsonOpenApiConfiguration
{
  /// <summary>
  /// Gets or sets the shared tag for the resource.
  /// </summary>
  public string? Tag { get; set; }

  /// <summary>
  /// Gets or sets the tag description.
  /// </summary>
  public string? TagDescription { get; set; }

  /// <summary>
  /// Gets or sets whether the resource is deprecated.
  /// </summary>
  public bool Deprecated { get; set; }

  /// <summary>
  /// Gets or sets the deprecation message.
  /// </summary>
  public string? DeprecationMessage { get; set; }

  /// <summary>
  /// Gets or sets the operation summaries.
  /// </summary>
  public Dictionary<string, string> Summaries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// Gets or sets the operation descriptions.
  /// </summary>
  public Dictionary<string, string> Descriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// JSON configuration for named hook handlers.
/// </summary>
public class RestLibJsonHookConfiguration
{
  /// <summary>
  /// Gets or sets the hooks that run when a request is first received.
  /// </summary>
  public RestLibJsonHookStage? OnRequestReceived { get; set; }

  /// <summary>
  /// Gets or sets the hooks that run after request validation.
  /// </summary>
  public RestLibJsonHookStage? OnRequestValidated { get; set; }

  /// <summary>
  /// Gets or sets the hooks that run before persistence.
  /// </summary>
  public RestLibJsonHookStage? BeforePersist { get; set; }

  /// <summary>
  /// Gets or sets the hooks that run after persistence.
  /// </summary>
  public RestLibJsonHookStage? AfterPersist { get; set; }

  /// <summary>
  /// Gets or sets the hooks that run before the response is sent.
  /// </summary>
  public RestLibJsonHookStage? BeforeResponse { get; set; }

  /// <summary>
  /// Gets or sets the hooks that run when an exception occurs.
  /// </summary>
  public RestLibJsonErrorHookStage? OnError { get; set; }
}

/// <summary>
/// JSON configuration for a standard hook stage.
/// </summary>
public class RestLibJsonHookStage
{
  /// <summary>
  /// Gets or sets hooks that always run for this stage.
  /// </summary>
  public List<string> Default { get; set; } = [];

  /// <summary>
  /// Gets or sets hooks that run only for specific operations.
  /// </summary>
  public Dictionary<string, List<string>> ByOperation { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// JSON configuration for the error hook stage.
/// </summary>
public class RestLibJsonErrorHookStage
{
  /// <summary>
  /// Gets or sets error hooks that always run.
  /// </summary>
  public List<string> Default { get; set; } = [];

  /// <summary>
  /// Gets or sets error hooks that run only for specific operations.
  /// </summary>
  public Dictionary<string, List<string>> ByOperation { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// JSON configuration for rate limiting policies.
/// </summary>
public class RestLibJsonRateLimitingConfiguration
{
  /// <summary>
  /// Gets or sets the default rate limiting policy applied to all operations.
  /// Per-operation overrides and disabled operations take precedence.
  /// </summary>
  public string? Default { get; set; }

  /// <summary>
  /// Gets or sets per-operation rate limiting policy overrides.
  /// Keys are operation names, values are policy names.
  /// </summary>
  public Dictionary<string, string> ByOperation { get; set; } = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// Gets or sets operations that are exempt from rate limiting.
  /// </summary>
  public List<RestLibOperation> Disabled { get; set; } = [];
}

/// <summary>
/// JSON configuration for batch operations.
/// </summary>
public class RestLibJsonBatchConfiguration
{
  /// <summary>
  /// Gets or sets the enabled batch actions.
  /// Valid values: Create, Update, Patch, Delete.
  /// If empty or omitted, all actions are enabled.
  /// </summary>
  public List<BatchAction> Actions { get; set; } = [];
}
