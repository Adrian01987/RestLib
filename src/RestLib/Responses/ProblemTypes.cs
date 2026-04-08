namespace RestLib.Responses;

/// <summary>
/// Standard problem type URIs for RestLib errors.
/// Constants hold the relative path portion; use <see cref="Resolve"/> to obtain
/// the full URI when a <see cref="Configuration.RestLibOptions.ProblemTypeBaseUri"/> is configured.
/// </summary>
public static class ProblemTypes
{
    /// <summary>Resource not found (404).</summary>
    public const string NotFound = "/problems/not-found";

    /// <summary>Validation failed (400).</summary>
    public const string ValidationFailed = "/problems/validation-failed";

    /// <summary>Bad request (400).</summary>
    public const string BadRequest = "/problems/bad-request";

    /// <summary>Invalid cursor format (400).</summary>
    public const string InvalidCursor = "/problems/invalid-cursor";

    /// <summary>Invalid pagination limit (400).</summary>
    public const string InvalidLimit = "/problems/invalid-limit";

    /// <summary>Invalid filter value (400).</summary>
    public const string InvalidFilter = "/problems/invalid-filter";

    /// <summary>Invalid sort parameter value (400).</summary>
    public const string InvalidSort = "/problems/invalid-sort";

    /// <summary>Invalid field selection parameters (400).</summary>
    public const string InvalidFields = "/problems/invalid-fields";

    /// <summary>Invalid batch request structure or action (400).</summary>
    public const string InvalidBatchRequest = "/problems/invalid-batch-request";

    /// <summary>Batch size exceeds the configured maximum (400).</summary>
    public const string BatchSizeExceeded = "/problems/batch-size-exceeded";

    /// <summary>Requested batch action is not enabled for this resource (400).</summary>
    public const string BatchActionNotEnabled = "/problems/batch-action-not-enabled";

    /// <summary>Resource conflict (409).</summary>
    public const string Conflict = "/problems/conflict";

    /// <summary>Precondition failed / ETag mismatch (412).</summary>
    public const string PreconditionFailed = "/problems/precondition-failed";

    /// <summary>Unauthorized / missing authentication (401).</summary>
    public const string Unauthorized = "/problems/unauthorized";

    /// <summary>Forbidden / insufficient permissions (403).</summary>
    public const string Forbidden = "/problems/forbidden";

    /// <summary>Internal server error (500).</summary>
    public const string InternalError = "/problems/internal-error";

    /// <summary>Hook short-circuited the operation (varies).</summary>
    public const string HookShortCircuit = "/problems/hook-short-circuit";

    private static string? _baseUri;

    /// <summary>
    /// Resolves a relative problem type path to its full URI.
    /// When no base URI is configured, the relative path is returned unchanged.
    /// </summary>
    /// <param name="relativeType">A relative problem type path (e.g., <c>/problems/not-found</c>).</param>
    /// <returns>The resolved URI string.</returns>
    public static string Resolve(string relativeType)
    {
        return _baseUri is null ? relativeType : _baseUri + relativeType;
    }

    /// <summary>
    /// Configures the base URI that is prepended to all relative problem type paths.
    /// Called once during service registration. Pass <c>null</c> to keep relative paths.
    /// </summary>
    /// <param name="baseUri">
    /// An absolute URI with no trailing slash (e.g., <c>https://api.example.com</c>), or <c>null</c>.
    /// </param>
    internal static void Configure(Uri? baseUri)
    {
        _baseUri = baseUri?.ToString().TrimEnd('/');
    }
}
