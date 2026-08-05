using System.Linq.Expressions;
using System.Reflection;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Builds the case-normalized operands used by EF Core string filters and search.
/// </summary>
internal static class StringQuerySemantics
{
    private static readonly MethodInfo StringToUpperMethod = typeof(string)
        .GetMethod(nameof(string.ToUpper), Type.EmptyTypes)
        ?? throw new InvalidOperationException("RestLib could not resolve string.ToUpper().");

    /// <summary>
    /// Builds the database-side case-normalization expression.
    /// </summary>
    /// <param name="source">The string property expression.</param>
    /// <returns>The normalized string expression.</returns>
    internal static Expression Normalize(Expression source)
    {
        return Expression.Call(source, StringToUpperMethod);
    }

    /// <summary>
    /// Normalizes a request value without depending on the server's current culture.
    /// </summary>
    /// <param name="value">The request value.</param>
    /// <returns>The invariantly normalized value.</returns>
    internal static string Normalize(string value)
    {
        return value.ToUpperInvariant();
    }
}
