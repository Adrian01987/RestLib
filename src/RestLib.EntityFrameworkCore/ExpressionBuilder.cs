using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using RestLib.Internal;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Builds LINQ expression trees for property access on entity types.
/// Used by filtering and sorting to construct IQueryable expressions
/// that translate to server-side SQL execution.
/// </summary>
public static class ExpressionBuilder
{
    private static readonly ConcurrentDictionary<(Type EntityType, string PropertyPath), Expression> Cache = new();

    /// <summary>
    /// Builds a property access expression for the specified property on the entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="propertyName">
    /// The name of the property to access. Lookup is case-insensitive.
    /// </param>
    /// <returns>
    /// An expression tree representing <c>entity => entity.PropertyName</c>,
    /// preserving the property's CLR type for query translation.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="propertyName"/> does not match any public instance
    /// property on <typeparamref name="TEntity"/>.
    /// </exception>
    public static LambdaExpression BuildPropertyAccess<TEntity>(string propertyName)
        where TEntity : class
    {
        PropertyPath propertyPath;
        try
        {
            propertyPath = NamingUtils.ResolvePropertyPath<TEntity>(propertyName, nameof(propertyName));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        var cacheKey = (typeof(TEntity), propertyPath.ClrPath);
        var expression = Cache.GetOrAdd(cacheKey, _ => BuildExpression<TEntity>(propertyPath.ClrSegments));

        return (LambdaExpression)expression;
    }

    private static LambdaExpression BuildExpression<TEntity>(IReadOnlyList<string> clrSegments)
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        Expression propertyAccess = parameter;
        var currentType = typeof(TEntity);

        foreach (var segment in clrSegments)
        {
            var property = currentType.GetProperty(
                segment,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new InvalidOperationException(
                    $"Property path '{string.Join('.', clrSegments)}' was not found on entity type '{typeof(TEntity).Name}'. Segment '{segment}' could not be resolved on '{currentType.Name}'.");

            propertyAccess = Expression.Property(propertyAccess, property);
            currentType = property.PropertyType;
        }

        var delegateType = typeof(Func<,>).MakeGenericType(typeof(TEntity), propertyAccess.Type);

        return Expression.Lambda(delegateType, propertyAccess, parameter);
    }
}
