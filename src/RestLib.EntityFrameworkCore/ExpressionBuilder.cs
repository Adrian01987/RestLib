using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Builds LINQ expression trees for property access on entity types.
/// Used by filtering and sorting to construct IQueryable expressions
/// that translate to server-side SQL execution.
/// </summary>
public static class ExpressionBuilder
{
    private static readonly ConcurrentDictionary<(Type EntityType, string PropertyName), Expression> Cache = new();

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
        var property = typeof(TEntity).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(
                $"Property '{propertyName}' was not found on entity type '{typeof(TEntity).Name}'.");

        var cacheKey = (typeof(TEntity), property.Name);
        var expression = Cache.GetOrAdd(cacheKey, _ => BuildExpression<TEntity>(property));

        return (LambdaExpression)expression;
    }

    private static LambdaExpression BuildExpression<TEntity>(PropertyInfo property)
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var propertyAccess = Expression.Property(parameter, property);
        var delegateType = typeof(Func<,>).MakeGenericType(typeof(TEntity), property.PropertyType);

        return Expression.Lambda(delegateType, propertyAccess, parameter);
    }
}
