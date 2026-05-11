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
    /// Resolves a validated scalar property path from a property access expression.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TProperty">The selected property type.</typeparam>
    /// <param name="expression">The property access expression.</param>
    /// <param name="parameterName">The parameter name for error reporting.</param>
    /// <returns>The resolved property path.</returns>
    internal static PropertyPath ResolvePropertyPath<TEntity, TProperty>(
        Expression<Func<TEntity, TProperty>> expression,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var properties = GetPropertyAccessChain(expression.Body, parameterName);
        return CreatePropertyPath(typeof(TEntity), string.Join('.', properties.Select(property => property.Name)), properties, parameterName);
    }

    /// <summary>
    /// Resolves a validated scalar property path from a dot-separated string.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="propertyPath">The CLR or query-style dot-separated path.</param>
    /// <param name="parameterName">The parameter name for error reporting.</param>
    /// <returns>The resolved property path.</returns>
    internal static PropertyPath ResolvePropertyPath<TEntity>(string propertyPath, string parameterName)
    {
        return ResolvePropertyPath(typeof(TEntity), propertyPath, parameterName);
    }

    /// <summary>
    /// Resolves a validated scalar property path from a dot-separated string.
    /// </summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="propertyPath">The CLR or query-style dot-separated path.</param>
    /// <param name="parameterName">The parameter name for error reporting.</param>
    /// <returns>The resolved property path.</returns>
    internal static PropertyPath ResolvePropertyPath(Type entityType, string propertyPath, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);

        var rawSegments = propertyPath.Split('.', StringSplitOptions.TrimEntries);
        if (rawSegments.Length == 0 || rawSegments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                $"Property path '{propertyPath}' is not a valid dot-separated property path.",
                parameterName);
        }

        var properties = new List<PropertyInfo>(rawSegments.Length);
        var currentType = entityType;
        foreach (var rawSegment in rawSegments)
        {
            var property = ResolvePropertySegment(currentType, rawSegment);
            if (property is null)
            {
                throw new ArgumentException(
                    $"Property path '{propertyPath}' was not found on entity type '{entityType.Name}'. Segment '{rawSegment}' could not be resolved on '{currentType.Name}'.",
                    parameterName);
            }

            properties.Add(property);
            currentType = property.PropertyType;
        }

        return CreatePropertyPath(entityType, propertyPath, properties, parameterName);
    }

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
        var memberExpression = UnwrapConvert(body) as MemberExpression ?? throw new ArgumentException(
                "Each expression must be a property access expression (e.g., p => p.PropertyName)",
                parameterName);
        return memberExpression;
    }

    /// <summary>
    /// Resolves a direct public instance property from a lambda expression.
    /// Nested member access is rejected.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="expression">The property access expression.</param>
    /// <param name="parameterName">The parameter name for error reporting.</param>
    /// <returns>The resolved <see cref="PropertyInfo"/>.</returns>
    internal static PropertyInfo GetDirectProperty<TEntity, TProperty>(
        Expression<Func<TEntity, TProperty>> expression,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var properties = GetPropertyAccessChain(expression.Body, parameterName);
        if (properties.Count != 1)
        {
            throw new ArgumentException(
                "The expression must access a direct property on the resource model (e.g., e => e.PropertyName).",
                parameterName);
        }

        return properties[0];
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

    private static PropertyPath CreatePropertyPath(
        Type entityType,
        string originalPath,
        IReadOnlyList<PropertyInfo> properties,
        string parameterName)
    {
        var clrSegments = new List<string>(properties.Count);
        var querySegments = new List<string>(properties.Count);

        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            clrSegments.Add(property.Name);
            querySegments.Add(ConvertToSnakeCase(property.Name));

            if (IsCollectionValued(property.PropertyType))
            {
                throw new ArgumentException(
                    $"Property path '{originalPath}' on entity type '{entityType.Name}' contains collection-valued segment '{property.Name}'. Collection-valued navigation paths are not supported.",
                    parameterName);
            }

            if (index < properties.Count - 1 && IsTerminalScalar(property.PropertyType))
            {
                throw new ArgumentException(
                    $"Property path '{originalPath}' on entity type '{entityType.Name}' cannot continue through scalar segment '{property.Name}'. Only nested reference-property paths are supported.",
                    parameterName);
            }
        }

        return new PropertyPath(
            string.Join('.', clrSegments),
            string.Join('.', querySegments),
            properties[^1].PropertyType,
            clrSegments,
            querySegments,
            hasCollectionSegment: false);
    }

    private static IReadOnlyList<PropertyInfo> GetPropertyAccessChain(Expression body, string parameterName)
    {
        var properties = new List<PropertyInfo>();
        var current = UnwrapConvert(body);

        while (current is MemberExpression memberExpression)
        {
            if (memberExpression.Member is not PropertyInfo property)
            {
                throw new ArgumentException(
                    "The expression must access a property.",
                    parameterName);
            }

            properties.Add(property);
            current = UnwrapConvert(memberExpression.Expression!);
        }

        if (current is not ParameterExpression)
        {
            throw new ArgumentException(
                "Each expression must be a property access expression (e.g., p => p.PropertyName)",
                parameterName);
        }

        properties.Reverse();
        return properties;
    }

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression unaryExpression
            && (unaryExpression.NodeType == ExpressionType.Convert
                || unaryExpression.NodeType == ExpressionType.ConvertChecked))
        {
            expression = unaryExpression.Operand;
        }

        return expression;
    }

    private static PropertyInfo? ResolvePropertySegment(Type entityType, string rawSegment)
    {
        return entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(property =>
                string.Equals(property.Name, rawSegment, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ConvertToSnakeCase(property.Name), rawSegment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCollectionValued(Type propertyType)
    {
        return propertyType != typeof(string)
            && propertyType != typeof(byte[])
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType);
    }

    private static bool IsTerminalScalar(Type propertyType)
    {
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        return underlyingType.IsPrimitive
            || underlyingType.IsEnum
            || underlyingType == typeof(string)
            || underlyingType == typeof(decimal)
            || underlyingType == typeof(Guid)
            || underlyingType == typeof(DateTime)
            || underlyingType == typeof(DateTimeOffset)
            || underlyingType == typeof(DateOnly)
            || underlyingType == typeof(TimeOnly)
            || underlyingType == typeof(TimeSpan);
    }
}
