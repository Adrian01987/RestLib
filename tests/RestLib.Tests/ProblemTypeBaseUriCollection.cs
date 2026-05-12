using Xunit;

namespace RestLib.Tests;

/// <summary>
/// Serializes tests that mutate the global <c>ProblemTypes</c> base URI.
/// </summary>
[CollectionDefinition("ProblemTypeBaseUri", DisableParallelization = true)]
public sealed class ProblemTypeBaseUriCollection
{
}
