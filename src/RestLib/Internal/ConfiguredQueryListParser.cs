namespace RestLib.Internal;

/// <summary>
/// Represents one comma-separated query segment after feature-specific tokenization.
/// </summary>
/// <param name="FieldName">The configured field name.</param>
/// <param name="Modifier">An optional feature-specific modifier.</param>
internal readonly record struct ConfiguredQueryToken(string FieldName, string? Modifier = null);

/// <summary>
/// Represents the feature-specific validation outcome for one configured query item.
/// </summary>
/// <typeparam name="TItem">The successfully parsed item type.</typeparam>
/// <typeparam name="TError">The validation error type.</typeparam>
/// <param name="Item">The parsed item, or <c>null</c> when validation failed.</param>
/// <param name="Error">The validation error, or <c>null</c> when validation succeeded.</param>
internal readonly record struct ConfiguredQueryItemParseResult<TItem, TError>(TItem? Item, TError? Error)
    where TItem : class
    where TError : class;

/// <summary>
/// Represents the outcome of parsing a configured comma-separated query value.
/// </summary>
/// <typeparam name="TItem">The successfully parsed item type.</typeparam>
/// <typeparam name="TError">The validation error type.</typeparam>
/// <param name="Items">The successfully parsed items.</param>
/// <param name="Errors">The validation errors.</param>
internal readonly record struct ConfiguredQueryListParseResult<TItem, TError>(
    IReadOnlyList<TItem> Items,
    IReadOnlyList<TError> Errors)
    where TItem : class
    where TError : class;

/// <summary>
/// Parses comma-separated query values whose items refer to a configured field allow-list.
/// </summary>
internal static class ConfiguredQueryListParser
{
    /// <summary>
    /// Parses, resolves, validates, and de-duplicates a configured comma-separated query value.
    /// </summary>
    /// <typeparam name="TProperty">The configured property metadata type.</typeparam>
    /// <typeparam name="TItem">The successfully parsed item type.</typeparam>
    /// <typeparam name="TError">The validation error type.</typeparam>
    /// <param name="value">The raw comma-separated query value.</param>
    /// <param name="properties">The configured property allow-list.</param>
    /// <param name="getQueryName">Returns the canonical query name for a configured property.</param>
    /// <param name="parseToken">Splits a query segment into its field name and optional modifier.</param>
    /// <param name="parseItem">Validates and creates a feature-specific item.</param>
    /// <param name="createUnknownError">Creates an error for an unknown field.</param>
    /// <param name="createDuplicateError">Creates an error for a repeated configured field.</param>
    /// <returns>The successfully parsed items and validation errors.</returns>
    internal static ConfiguredQueryListParseResult<TItem, TError> Parse<TProperty, TItem, TError>(
        string? value,
        IReadOnlyList<TProperty> properties,
        Func<TProperty, string> getQueryName,
        Func<string, ConfiguredQueryToken> parseToken,
        Func<ConfiguredQueryToken, TProperty, ConfiguredQueryItemParseResult<TItem, TError>> parseItem,
        Func<string, string, TError> createUnknownError,
        Func<string, TError> createDuplicateError)
        where TProperty : class
        where TItem : class
        where TError : class
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ConfiguredQueryListParseResult<TItem, TError>([], []);
        }

        var items = new List<TItem>();
        var errors = new List<TError>();
        var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? allowedNames = null;

        foreach (var segment in value.Split(','))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var token = parseToken(trimmed);
            var property = properties.FirstOrDefault(candidate =>
                string.Equals(getQueryName(candidate), token.FieldName, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                allowedNames ??= string.Join(", ", properties.Select(getQueryName));
                errors.Add(createUnknownError(token.FieldName, allowedNames));
                continue;
            }

            var itemResult = parseItem(token, property);
            if (itemResult.Error is not null)
            {
                errors.Add(itemResult.Error);
                continue;
            }

            var canonicalName = getQueryName(property);
            if (!seenFields.Add(canonicalName))
            {
                errors.Add(createDuplicateError(token.FieldName));
                continue;
            }

            items.Add(itemResult.Item!);
        }

        return new ConfiguredQueryListParseResult<TItem, TError>(items, errors);
    }
}
