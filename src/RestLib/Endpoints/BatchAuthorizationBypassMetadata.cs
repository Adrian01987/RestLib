using Microsoft.AspNetCore.Authorization;

namespace RestLib.Endpoints;

/// <summary>
/// Lets the shared batch route reach its action-aware authorization check.
/// </summary>
internal sealed class BatchAuthorizationBypassMetadata : IAllowAnonymous
{
    private BatchAuthorizationBypassMetadata()
    {
    }

    /// <summary>
    /// Gets the shared metadata instance.
    /// </summary>
    internal static BatchAuthorizationBypassMetadata Instance { get; } = new();
}
