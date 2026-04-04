using System.Collections.Concurrent;

namespace RestLib.Configuration;

/// <summary>
/// Collects OpenAPI tag descriptions registered during endpoint configuration.
/// These descriptions are applied to the document-level <c>tags</c> array by
/// <see cref="TagDescriptionDocumentTransformer"/>.
/// </summary>
internal sealed class TagDescriptionRegistry
{
    private readonly ConcurrentDictionary<string, string> _descriptions = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a description for the given tag name.
    /// If a description is already registered for the same tag, it is overwritten.
    /// </summary>
    /// <param name="tagName">The OpenAPI tag name.</param>
    /// <param name="description">The tag description.</param>
    internal void Set(string tagName, string description)
    {
        _descriptions[tagName] = description;
    }

    /// <summary>
    /// Returns all registered tag descriptions as a read-only snapshot.
    /// </summary>
    internal IReadOnlyDictionary<string, string> GetAll()
    {
        return new Dictionary<string, string>(_descriptions, StringComparer.Ordinal);
    }
}
