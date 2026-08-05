namespace RestLib.Filtering;

/// <summary>
/// Defines the filter operator and property-type combinations that RestLib can
/// evaluate consistently across its built-in repository adapters.
/// </summary>
internal static class FilterOperatorCompatibility
{
    /// <summary>
    /// Determines whether an operator performs an ordered comparison.
    /// </summary>
    /// <param name="filterOperator">The filter operator.</param>
    /// <returns><c>true</c> for relational operators; otherwise, <c>false</c>.</returns>
    internal static bool IsRelational(FilterOperator filterOperator)
    {
        return filterOperator is FilterOperator.Gt or FilterOperator.Lt or FilterOperator.Gte or FilterOperator.Lte;
    }

    /// <summary>
    /// Determines whether an operator can be applied to a property type.
    /// </summary>
    /// <param name="filterOperator">The requested filter operator.</param>
    /// <param name="propertyType">The configured property type.</param>
    /// <returns><c>true</c> when the combination is supported; otherwise, <c>false</c>.</returns>
    internal static bool IsSupported(FilterOperator filterOperator, Type propertyType)
    {
        ArgumentNullException.ThrowIfNull(propertyType);

        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        return filterOperator switch
        {
            FilterOperator.Gt or
            FilterOperator.Lt or
            FilterOperator.Gte or
            FilterOperator.Lte => SupportsRelationalComparison(underlyingType),
            FilterOperator.Contains or
            FilterOperator.StartsWith or
            FilterOperator.EndsWith => underlyingType == typeof(string),
            _ => true,
        };
    }

    /// <summary>
    /// Determines whether a type belongs to RestLib's portable relational-comparison baseline.
    /// </summary>
    /// <param name="propertyType">The non-nullable property type.</param>
    /// <returns><c>true</c> when relational operators are supported; otherwise, <c>false</c>.</returns>
    internal static bool SupportsRelationalComparison(Type propertyType)
    {
        return propertyType == typeof(byte)
            || propertyType == typeof(short)
            || propertyType == typeof(int)
            || propertyType == typeof(long)
            || propertyType == typeof(float)
            || propertyType == typeof(double)
            || propertyType == typeof(decimal)
            || propertyType == typeof(DateTime);
    }
}
