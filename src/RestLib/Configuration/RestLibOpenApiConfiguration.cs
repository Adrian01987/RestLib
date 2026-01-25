namespace RestLib.Configuration;

/// <summary>
/// Configuration options for OpenAPI metadata per resource.
/// </summary>
public class RestLibOpenApiConfiguration
{
  /// <summary>
  /// Gets or sets the custom tag for all endpoints of this resource.
  /// If not set, the entity type name will be used.
  /// </summary>
  /// <example>
  /// <code>
  /// config.OpenApi.Tag = "Products";
  /// </code>
  /// </example>
  public string? Tag { get; set; }

  /// <summary>
  /// Gets or sets a custom tag description for OpenAPI documentation.
  /// </summary>
  public string? TagDescription { get; set; }

  /// <summary>
  /// Gets or sets whether the resource is deprecated.
  /// When true, all endpoints will be marked as deprecated in OpenAPI documentation.
  /// </summary>
  /// <example>
  /// <code>
  /// config.OpenApi.Deprecated = true;
  /// </code>
  /// </example>
  public bool Deprecated { get; set; }

  /// <summary>
  /// Gets or sets a deprecation message to include in the description when deprecated.
  /// </summary>
  public string? DeprecationMessage { get; set; }

  /// <summary>
  /// Gets the operation-specific summaries configuration.
  /// </summary>
  public RestLibOpenApiSummaries Summaries { get; } = new();

  /// <summary>
  /// Gets the operation-specific descriptions configuration.
  /// </summary>
  public RestLibOpenApiDescriptions Descriptions { get; } = new();
}

/// <summary>
/// Custom summaries for OpenAPI operations.
/// </summary>
public class RestLibOpenApiSummaries
{
  /// <summary>
  /// Gets or sets the summary for the GetAll operation.
  /// </summary>
  public string? GetAll { get; set; }

  /// <summary>
  /// Gets or sets the summary for the GetById operation.
  /// </summary>
  public string? GetById { get; set; }

  /// <summary>
  /// Gets or sets the summary for the Create operation.
  /// </summary>
  public string? Create { get; set; }

  /// <summary>
  /// Gets or sets the summary for the Update operation.
  /// </summary>
  public string? Update { get; set; }

  /// <summary>
  /// Gets or sets the summary for the Patch operation.
  /// </summary>
  public string? Patch { get; set; }

  /// <summary>
  /// Gets or sets the summary for the Delete operation.
  /// </summary>
  public string? Delete { get; set; }

  /// <summary>
  /// Gets the summary for the specified operation.
  /// </summary>
  internal string? GetSummary(RestLibOperation operation) => operation switch
  {
    RestLibOperation.GetAll => GetAll,
    RestLibOperation.GetById => GetById,
    RestLibOperation.Create => Create,
    RestLibOperation.Update => Update,
    RestLibOperation.Patch => Patch,
    RestLibOperation.Delete => Delete,
    _ => null
  };
}

/// <summary>
/// Custom descriptions for OpenAPI operations.
/// </summary>
public class RestLibOpenApiDescriptions
{
  /// <summary>
  /// Gets or sets the description for the GetAll operation.
  /// </summary>
  public string? GetAll { get; set; }

  /// <summary>
  /// Gets or sets the description for the GetById operation.
  /// </summary>
  public string? GetById { get; set; }

  /// <summary>
  /// Gets or sets the description for the Create operation.
  /// </summary>
  public string? Create { get; set; }

  /// <summary>
  /// Gets or sets the description for the Update operation.
  /// </summary>
  public string? Update { get; set; }

  /// <summary>
  /// Gets or sets the description for the Patch operation.
  /// </summary>
  public string? Patch { get; set; }

  /// <summary>
  /// Gets or sets the description for the Delete operation.
  /// </summary>
  public string? Delete { get; set; }

  /// <summary>
  /// Gets the description for the specified operation.
  /// </summary>
  internal string? GetDescription(RestLibOperation operation) => operation switch
  {
    RestLibOperation.GetAll => GetAll,
    RestLibOperation.GetById => GetById,
    RestLibOperation.Create => Create,
    RestLibOperation.Update => Update,
    RestLibOperation.Patch => Patch,
    RestLibOperation.Delete => Delete,
    _ => null
  };
}
