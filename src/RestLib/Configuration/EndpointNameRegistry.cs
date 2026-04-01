using System.Collections.Concurrent;

namespace RestLib.Configuration;

/// <summary>
/// Tracks endpoint name usage to ensure unique OpenAPI operation IDs across
/// multiple <c>MapRestLib</c> registrations within a single application host.
/// Registered as a singleton by <see cref="RestLibServiceExtensions.AddRestLib"/>
/// so that each test host gets its own isolated instance.
/// </summary>
internal sealed class EndpointNameRegistry
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    /// <summary>
    /// Returns a unique endpoint name prefix for the given candidate.
    /// On the first call for a candidate the candidate itself is returned.
    /// Subsequent calls append an incrementing numeric suffix (e.g. <c>Product2</c>, <c>Product3</c>).
    /// </summary>
    /// <param name="candidateName">The base candidate name derived from entity type and route prefix.</param>
    /// <returns>A globally unique (within this registry) endpoint name prefix.</returns>
    internal string GetUniqueEndpointName(string candidateName)
    {
        var count = _counts.AddOrUpdate(candidateName, 1, (_, existing) => existing + 1);
        return count > 1 ? $"{candidateName}{count}" : candidateName;
    }
}
