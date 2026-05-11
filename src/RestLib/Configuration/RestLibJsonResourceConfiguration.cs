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
    /// Gets or sets the assembly-qualified CLR API model type name used by
    /// folder-based loading. For single-model resources, this is the entity
    /// type exposed and persisted by the resource.
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Gets or sets the optional API-to-DB model mapping configuration for a
    /// two-model resource.
    /// </summary>
    public RestLibJsonMappingConfiguration? Mapping { get; set; }

    /// <summary>
    /// Gets or sets the property name used as the entity key.
    /// Defaults to <c>Id</c> when omitted.
    /// </summary>
    public string? KeyProperty { get; set; }

    /// <summary>
    /// Gets or sets the ordered composite-key configuration.
    /// </summary>
    public RestLibJsonKeyConfiguration? Key { get; set; }

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
    /// Gets or sets the filterable entity property names (equality-only).
    /// Entries can be direct CLR property names or dot-separated nested
    /// reference-property paths (for example, <c>Customer.Email</c>). Query
    /// parameter names use snake_case per segment joined with dots (for example,
    /// <c>customer.email</c>). For operator-based filtering, use
    /// <see cref="FilteringOperators"/> instead. When a property appears in
    /// both, the <see cref="FilteringOperators"/> entry takes precedence.
    /// </summary>
    public List<string> Filtering { get; set; } = [];

    /// <summary>
    /// Gets or sets per-property filter operator configuration.
    /// Keys are CLR property names or dot-separated nested reference-property
    /// paths, values are lists of operator names (e.g., "eq", "neq", "gt",
    /// "lt", "gte", "lte", "contains", "starts_with", "in"). Query
    /// parameter names use snake_case per segment joined with dots.
    /// </summary>
    public Dictionary<string, List<string>> FilteringOperators { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the sortable entity property names.
    /// Entries can be direct CLR property names or dot-separated nested
    /// reference-property paths. Query parameter names use snake_case per
    /// segment joined with dots.
    /// </summary>
    public List<string> Sorting { get; set; } = [];

    /// <summary>
    /// Gets or sets the default sort expression (e.g. "name:asc,price:desc"
    /// or "customer.name:asc").
    /// </summary>
    public string? DefaultSort { get; set; }

    /// <summary>
    /// Gets or sets the selectable entity property names for sparse fieldsets.
    /// Entries can be direct CLR property names or dot-separated nested
    /// reference-property paths. Query parameter names use snake_case per
    /// segment joined with dots. Nested selections serialize with dotted output
     /// keys (for example, <c>customer.email</c>).
     /// </summary>
    public List<string> FieldSelection { get; set; } = [];

    /// <summary>
    /// Gets or sets the searchable entity property names for OR-of-contains search.
    /// Entries can be direct CLR property names or dot-separated nested reference-property
    /// paths. Search is available on collection endpoints only and uses the configured query
    /// parameter, which defaults to <c>q</c>.
    /// </summary>
    public List<string> Search { get; set; } = [];

    /// <summary>
    /// Gets or sets optional search behavior overrides.
    /// </summary>
    public RestLibJsonSearchOptionsConfiguration? SearchOptions { get; set; }

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

    /// <summary>
    /// Gets or sets JSON-declared validation rules keyed by CLR property name.
    /// </summary>
    public Dictionary<string, RestLibJsonValidationRuleConfiguration> Validation { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// JSON configuration for validation rules applied to a single property.
/// </summary>
public class RestLibJsonValidationRuleConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the property is required.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Gets or sets the minimum numeric value allowed for the property.
    /// </summary>
    public decimal? Min { get; set; }

    /// <summary>
    /// Gets or sets the maximum numeric value allowed for the property.
    /// </summary>
    public decimal? Max { get; set; }

    /// <summary>
    /// Gets or sets the string length validation configuration.
    /// </summary>
    public RestLibJsonLengthValidationConfiguration? Length { get; set; }

    /// <summary>
    /// Gets or sets the regular expression pattern the string value must match.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the string value must be a valid email address.
    /// </summary>
    public bool Email { get; set; }
}

/// <summary>
/// JSON configuration for API-to-DB model mapping.
/// </summary>
public class RestLibJsonMappingConfiguration
{
    /// <summary>
    /// Gets or sets the assembly-qualified CLR DB model type name used by
    /// folder-based loading.
    /// </summary>
    public string? DbType { get; set; }

    /// <summary>
    /// Gets or sets the mapper implementation type name used to select a named
    /// mapper registration.
    /// </summary>
    public string? Mapper { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether RestLib should use the built-in
    /// strict reflection mapper for this resource.
    /// </summary>
    public bool Auto { get; set; }

    /// <summary>
    /// Gets or sets the hook model used by named JSON hooks. Valid values are
    /// <c>Api</c> and <c>Db</c>.
    /// </summary>
    public string? HookModel { get; set; }
}

/// <summary>
/// JSON configuration for an ordered two-part composite key.
/// </summary>
public class RestLibJsonKeyConfiguration
{
    /// <summary>
    /// Gets or sets the ordered CLR property names that make up the key.
    /// </summary>
    public List<string> Properties { get; set; } = [];

    /// <summary>
    /// Gets or sets the ordered route parameter names used by the resource route.
    /// </summary>
    public List<string> RouteParameters { get; set; } = [];
}

/// <summary>
/// JSON configuration for string length validation.
/// </summary>
public class RestLibJsonLengthValidationConfiguration
{
    /// <summary>
    /// Gets or sets the minimum allowed string length.
    /// </summary>
    public int? Min { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed string length.
    /// </summary>
    public int? Max { get; set; }
}

/// <summary>
/// JSON configuration for collection search behavior.
/// </summary>
public class RestLibJsonSearchOptionsConfiguration
{
    /// <summary>
    /// Gets or sets the query parameter name used for search.
    /// Defaults to <c>q</c> when omitted.
    /// </summary>
    public string? QueryParameter { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether search uses case-sensitive matching.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool CaseSensitive { get; set; }
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
