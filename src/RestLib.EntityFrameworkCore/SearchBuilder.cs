using System.Linq.Expressions;
using System.Reflection;
using RestLib.Search;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Builds search predicates as LINQ expression trees.
/// </summary>
internal static class SearchBuilder
{
    private static readonly MethodInfo StringContainsMethod = typeof(string)
        .GetMethod(nameof(string.Contains), [typeof(string)])
        ?? throw new InvalidOperationException("RestLib could not resolve string.Contains(string).");
    private static readonly MethodInfo StringToLowerMethod = typeof(string)
        .GetMethod(nameof(string.ToLower), Type.EmptyTypes)
        ?? throw new InvalidOperationException("RestLib could not resolve string.ToLower().");

    /// <summary>
    /// Builds a search predicate for the specified search request.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="search">The active search request.</param>
    /// <returns>An expression tree representing the configured OR-of-contains search.</returns>
    internal static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(SearchRequest search)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(search);

        if (search.Properties.Count == 0)
        {
            return static _ => true;
        }

        var term = search.CaseSensitive ? search.Term : search.Term.ToLower();
        Expression? body = null;
        var parameter = Expression.Parameter(typeof(TEntity), "entity");

        foreach (var property in search.Properties)
        {
            var propertyAccess = ExpressionBuilder.BuildPropertyAccess<TEntity>(property.PropertyName);
            var propertyBody = ReplaceParameter(propertyAccess.Body, propertyAccess.Parameters[0], parameter);

            var nullConstant = Expression.Constant(null, typeof(string));
            var notNull = Expression.NotEqual(propertyBody, nullConstant);
            var comparisonSource = search.CaseSensitive
                ? propertyBody
                : Expression.Call(propertyBody, StringToLowerMethod);
            var contains = Expression.Call(
                comparisonSource,
                StringContainsMethod,
                Expression.Constant(term, typeof(string)));
            var propertyPredicate = Expression.AndAlso(notNull, contains);
            body = body is null ? propertyPredicate : Expression.OrElse(body, propertyPredicate);
        }

        return Expression.Lambda<Func<TEntity, bool>>(body!, parameter);
    }

    private static Expression ReplaceParameter(
        Expression body,
        ParameterExpression source,
        ParameterExpression target)
    {
        return new ParameterReplaceVisitor(source, target).Visit(body)
            ?? throw new InvalidOperationException("RestLib could not rewrite the search expression parameter.");
    }

    private sealed class ParameterReplaceVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _source;
        private readonly ParameterExpression _target;

        internal ParameterReplaceVisitor(ParameterExpression source, ParameterExpression target)
        {
            _source = source;
            _target = target;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _source ? _target : base.VisitParameter(node);
        }
    }
}
