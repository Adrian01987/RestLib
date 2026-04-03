using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace RestLib.Internal;

/// <summary>
/// Shared utility methods for naming conventions and expression handling.
/// </summary>
internal static class NamingUtils
{
    /// <summary>
    /// Converts a PascalCase property name to snake_case using the built-in
    /// <see cref="JsonNamingPolicy.SnakeCaseLower"/> policy for consistency.
    /// </summary>
    /// <param name="propertyName">The PascalCase property name.</param>
    /// <returns>The snake_case equivalent.</returns>
    internal static string ConvertToSnakeCase(string propertyName) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(propertyName);

    /// <summary>
    /// Extracts the <see cref="MemberExpression"/> from a lambda expression body,
    /// handling the <see cref="UnaryExpression"/> (Convert) wrapper that the compiler
    /// adds for value-type properties in <c>Expression&lt;Func&lt;T, object?&gt;&gt;</c> lambdas.
    /// </summary>
    /// <param name="body">The expression body to extract from.</param>
    /// <param name="parameterName">The parameter name for error reporting.</param>
    /// <returns>The extracted <see cref="MemberExpression"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the expression is not a property access expression.
    /// </exception>
    internal static MemberExpression GetMemberExpression(Expression body, string parameterName)
    {
        var memberExpression = (body as MemberExpression
            ?? (body as UnaryExpression)?.Operand as MemberExpression) ?? throw new ArgumentException(
                "Each expression must be a property access expression (e.g., p => p.PropertyName)",
                parameterName);
        return memberExpression;
    }

    /// <summary>
    /// Resolves a property on an entity type by name, throwing a descriptive error if not found.
    /// </summary>
    /// <typeparam name="TEntity">The entity type to inspect.</typeparam>
    /// <param name="propertyName">The property name to look up.</param>
    /// <param name="parameterName">The parameter name for error reporting.</param>
    /// <returns>The resolved <see cref="PropertyInfo"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the property is not found on the entity type.
    /// </exception>
    internal static PropertyInfo ResolveProperty<TEntity>(string propertyName, string parameterName)
    {
        return typeof(TEntity).GetProperty(propertyName)
            ?? throw new ArgumentException(
                $"Property '{propertyName}' was not found on entity type '{typeof(TEntity).Name}'.",
                parameterName);
    }
}
