namespace RestLib.Filtering;

/// <summary>
/// Provides predefined sets of <see cref="FilterOperator"/> values for common use cases.
/// </summary>
public static class FilterOperators
{
    /// <summary>
    /// Equality operators: <see cref="FilterOperator.Eq"/> and <see cref="FilterOperator.Neq"/>.
    /// </summary>
    public static readonly IReadOnlyList<FilterOperator> Equality =
        Array.AsReadOnly(new[] { FilterOperator.Eq, FilterOperator.Neq });

    /// <summary>
    /// Comparison operators suitable for numeric, date, and other <see cref="IComparable"/> types:
    /// <see cref="FilterOperator.Eq"/>, <see cref="FilterOperator.Neq"/>,
    /// <see cref="FilterOperator.Gt"/>, <see cref="FilterOperator.Lt"/>,
    /// <see cref="FilterOperator.Gte"/>, <see cref="FilterOperator.Lte"/>.
    /// </summary>
    public static readonly IReadOnlyList<FilterOperator> Comparison = Array.AsReadOnly(new[]
    {
        FilterOperator.Eq, FilterOperator.Neq,
        FilterOperator.Gt, FilterOperator.Lt,
        FilterOperator.Gte, FilterOperator.Lte,
    });

    /// <summary>
    /// String operators:
    /// <see cref="FilterOperator.Eq"/>, <see cref="FilterOperator.Neq"/>,
    /// <see cref="FilterOperator.Contains"/>, <see cref="FilterOperator.StartsWith"/>,
    /// <see cref="FilterOperator.EndsWith"/>.
    /// </summary>
    public static readonly IReadOnlyList<FilterOperator> String = Array.AsReadOnly(new[]
    {
        FilterOperator.Eq, FilterOperator.Neq,
        FilterOperator.Contains, FilterOperator.StartsWith, FilterOperator.EndsWith,
    });

    /// <summary>
    /// All operators defined in <see cref="FilterOperator"/>.
    /// </summary>
    public static readonly IReadOnlyList<FilterOperator> All =
        Array.AsReadOnly(Enum.GetValues<FilterOperator>());
}
