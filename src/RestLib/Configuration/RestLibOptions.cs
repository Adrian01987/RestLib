using System.Text.Json;

namespace RestLib.Configuration;

/// <summary>
/// Configuration options for RestLib.
/// </summary>
public class RestLibOptions
{
    /// <summary>
    /// Gets or sets the JSON naming policy for serialization.
    /// Defaults to snake_case per Zalando Rule 118.
    /// </summary>
    public JsonNamingPolicy JsonNamingPolicy { get; set; } = JsonNamingPolicy.SnakeCaseLower;

    /// <summary>
    /// Gets or sets whether null values should be omitted from JSON responses.
    /// Defaults to true.
    /// </summary>
    public bool OmitNullValues { get; set; } = true;

    /// <summary>
    /// Gets or sets the default page size for pagination.
    /// Defaults to 20.
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Gets or sets the maximum page size for pagination.
    /// Defaults to 100.
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether pagination links should be included in responses.
    /// Defaults to true.
    /// </summary>
    public bool IncludePaginationLinks { get; set; } = true;

    /// <summary>
    /// Gets or sets whether authorization is required by default.
    /// Defaults to true (secure by default per ADR-002).
    /// </summary>
    public bool RequireAuthorizationByDefault { get; set; } = true;

    /// <summary>
    /// Gets or sets whether ETag support is enabled.
    /// Defaults to false.
    /// </summary>
    public bool EnableETagSupport { get; set; } = false;

    /// <summary>
    /// Gets or sets whether Problem Details (RFC 9457) should be used for errors.
    /// Defaults to true.
    /// </summary>
    public bool UseProblemDetails { get; set; } = true;

    /// <summary>
    /// Gets or sets whether exception details should be included in error responses.
    /// Should be false in production. Defaults to false.
    /// </summary>
    public bool IncludeExceptionDetailsInErrors { get; set; } = false;

    /// <summary>
    /// Gets or sets whether Data Annotation validation is enabled.
    /// When true, entities are validated before Create and Update operations.
    /// Defaults to true.
    /// </summary>
    public bool EnableValidation { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of items allowed in a single batch request.
    /// Defaults to 100. Set to 0 to disable the limit.
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of values allowed in a filter <c>in</c> operator list.
    /// Defaults to 50.
    /// </summary>
    public int MaxFilterInListSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the maximum allowed length (in characters) of a cursor query-string value.
    /// Cursors exceeding this length are rejected with a 400 Problem Details response before
    /// any base64 decoding or JSON parsing occurs. Defaults to 4096.
    /// </summary>
    public int MaxCursorLength { get; set; } = 4096;

    /// <summary>
    /// Gets or sets the base URI prepended to problem type relative paths.
    /// When set, problem type URIs change from relative paths like <c>/problems/not-found</c>
    /// to absolute URIs like <c>https://api.example.com/problems/not-found</c>.
    /// Must be an absolute URI with no trailing slash. Defaults to <c>null</c> (relative paths).
    /// </summary>
    public Uri? ProblemTypeBaseUri { get; set; }

    /// <summary>
    /// Gets or sets whether HATEOAS (hypermedia links) are included in entity responses.
    /// When enabled, each entity response includes a <c>_links</c> object with HAL-style
    /// links for <c>self</c>, <c>collection</c>, and enabled CRUD operations.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool EnableHateoas { get; set; } = false;
}
