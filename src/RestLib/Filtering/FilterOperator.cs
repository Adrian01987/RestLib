namespace RestLib.Filtering;

/// <summary>
/// Defines the comparison operators available for filter expressions.
/// </summary>
public enum FilterOperator
{
    /// <summary>Equality (default). Query syntax: <c>?field=value</c> or <c>?field[eq]=value</c>.</summary>
    Eq,

    /// <summary>Not equal. Query syntax: <c>?field[neq]=value</c>.</summary>
    Neq,

    /// <summary>Greater than. Query syntax: <c>?field[gt]=value</c>. Valid for portable numeric and <see cref="DateTime"/> properties.</summary>
    Gt,

    /// <summary>Less than. Query syntax: <c>?field[lt]=value</c>. Valid for portable numeric and <see cref="DateTime"/> properties.</summary>
    Lt,

    /// <summary>Greater than or equal. Query syntax: <c>?field[gte]=value</c>. Valid for portable numeric and <see cref="DateTime"/> properties.</summary>
    Gte,

    /// <summary>Less than or equal. Query syntax: <c>?field[lte]=value</c>. Valid for portable numeric and <see cref="DateTime"/> properties.</summary>
    Lte,

    /// <summary>Case-insensitive literal substring match. Query syntax: <c>?field[contains]=value</c>. Valid for strings only.</summary>
    Contains,

    /// <summary>Case-insensitive literal prefix match. Query syntax: <c>?field[starts_with]=value</c>. Valid for strings only.</summary>
    StartsWith,

    /// <summary>Case-insensitive literal suffix match. Query syntax: <c>?field[ends_with]=value</c>. Valid for strings only.</summary>
    EndsWith,

    /// <summary>Value is one of a comma-separated list. Query syntax: <c>?field[in]=a,b,c</c>.</summary>
    In,
}
