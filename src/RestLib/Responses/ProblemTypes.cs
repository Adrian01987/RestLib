namespace RestLib.Responses;

/// <summary>
/// Standard problem type URIs for RestLib errors.
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
}
