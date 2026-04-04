using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RestLib.Configuration;

/// <summary>
/// An OpenAPI document transformer that applies tag descriptions collected
/// by <see cref="TagDescriptionRegistry"/> to the document-level <c>tags</c> array.
/// </summary>
internal sealed class TagDescriptionDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly TagDescriptionRegistry _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="TagDescriptionDocumentTransformer"/> class.
    /// </summary>
    /// <param name="registry">The registry containing tag descriptions.</param>
    public TagDescriptionDocumentTransformer(TagDescriptionRegistry registry)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var descriptions = _registry.GetAll();
        if (descriptions.Count == 0)
        {
            return Task.CompletedTask;
        }

        document.Tags ??= new HashSet<OpenApiTag>();

        foreach (var (tagName, description) in descriptions)
        {
            var existing = document.Tags.FirstOrDefault(t =>
                string.Equals(t.Name, tagName, StringComparison.Ordinal));

            if (existing is not null)
            {
                // Tag already exists — remove and re-add with description
                // because OpenApiTag.Description may not be settable on all implementations.
                document.Tags.Remove(existing);
            }

            document.Tags.Add(new OpenApiTag
            {
                Name = tagName,
                Description = description
            });
        }

        return Task.CompletedTask;
    }
}
