using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RestLib.EntityFrameworkCore;

/// <summary>
/// Resolves EF Core resource-key metadata and builds key access and predicate operations.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The resource-key type.</typeparam>
internal sealed class EfCoreKeyMetadata<TEntity, TKey>
    where TEntity : class
    where TKey : notnull
{
    private readonly IEntityType _entityType;
    private readonly IReadOnlyList<KeyPart> _keyParts;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfCoreKeyMetadata{TEntity, TKey}"/> class.
    /// </summary>
    /// <param name="model">The EF Core model containing the entity metadata.</param>
    /// <param name="keySelector">The optional explicit resource-key selector.</param>
    internal EfCoreKeyMetadata(
        IModel model,
        Expression<Func<TEntity, TKey>>? keySelector)
    {
        ArgumentNullException.ThrowIfNull(model);

        _entityType = model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' is not part of the EF Core model.");
        PrimaryKey = _entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' has no primary key configured in the EF Core model.");
        UsesExplicitKeySelector = keySelector is not null;

        var resolvedKey = ResolveKey(keySelector);
        KeyAccessor = resolvedKey.KeyAccessor;
        _keyParts = resolvedKey.KeyParts;
    }

    /// <summary>
    /// Gets a value indicating whether an explicit resource-key selector is used.
    /// </summary>
    internal bool UsesExplicitKeySelector { get; }

    /// <summary>
    /// Gets the EF Core primary key for the entity type.
    /// </summary>
    internal IKey PrimaryKey { get; }

    /// <summary>
    /// Gets the compiled resource-key accessor.
    /// </summary>
    internal Func<TEntity, TKey> KeyAccessor { get; }

    /// <summary>
    /// Gets the ordered CLR property names that compose the resource key.
    /// </summary>
    internal IReadOnlyList<string> PropertyNames => _keyParts
        .Select(part => part.PropertyName)
        .ToList();

    /// <summary>
    /// Gets the ordered key selectors used as stable sort tie-breakers.
    /// </summary>
    internal IReadOnlyList<SortBuilder.SortKeyPart> SortKeyParts => _keyParts
        .Select(part => new SortBuilder.SortKeyPart(part.PropertyName, part.Selector))
        .ToList();

    /// <summary>
    /// Gets the ordered EF Core key values represented by a resource key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The ordered key values.</returns>
    internal object?[] GetKeyValues(TKey key)
    {
        return _keyParts
            .Select(keyPart => keyPart.GetKeyValue(key))
            .ToArray();
    }

    /// <summary>
    /// Builds a predicate that matches one resource key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The key-equality predicate.</returns>
    internal Expression<Func<TEntity, bool>> BuildEqualsPredicate(TKey key)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var comparisons = _keyParts
            .Select(keyPart => BuildEntityKeyPartEqualsExpression(
                parameter,
                keyPart,
                keyPart.GetKeyValue(key)))
            .ToList();

        var predicateBody = comparisons.Aggregate(Expression.AndAlso);
        return Expression.Lambda<Func<TEntity, bool>>(predicateBody, parameter);
    }

    /// <summary>
    /// Builds a predicate that matches any of the supplied resource keys.
    /// </summary>
    /// <param name="keys">The resource keys.</param>
    /// <returns>The combined key predicate.</returns>
    internal Expression<Func<TEntity, bool>> BuildContainsPredicate(IReadOnlyList<TKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        Expression? predicate = null;

        foreach (var key in keys)
        {
            Expression? keyPredicate = null;
            foreach (var keyPart in _keyParts)
            {
                var equals = BuildEntityKeyPartEqualsExpression(
                    parameter,
                    keyPart,
                    keyPart.GetKeyValue(key));
                keyPredicate = keyPredicate is null ? equals : Expression.AndAlso(keyPredicate, equals);
            }

            predicate = predicate is null ? keyPredicate : Expression.OrElse(predicate, keyPredicate!);
        }

        return Expression.Lambda<Func<TEntity, bool>>(predicate!, parameter);
    }

    /// <summary>
    /// Copies EF Core primary-key values from the current entity to its replacement.
    /// </summary>
    /// <param name="source">The current entity.</param>
    /// <param name="target">The replacement entity.</param>
    internal void CopyPrimaryKeyValues(TEntity source, TEntity target)
    {
        foreach (var keyProperty in PrimaryKey.Properties)
        {
            if (keyProperty.PropertyInfo is null)
            {
                continue;
            }

            var keyValue = keyProperty.PropertyInfo.GetValue(source);
            keyProperty.PropertyInfo.SetValue(target, keyValue);
        }
    }

    /// <summary>
    /// Assigns every resource-key part to a tracked entity entry.
    /// </summary>
    /// <param name="entry">The tracked entity entry.</param>
    /// <param name="key">The resource key.</param>
    internal void SetResourceKeyValues(EntityEntry<TEntity> entry, TKey key)
    {
        ArgumentNullException.ThrowIfNull(entry);

        foreach (var keyPart in _keyParts)
        {
            entry.Property(keyPart.PropertyName).CurrentValue = keyPart.GetKeyValue(key);
        }
    }

    private static Expression BuildPropertyAccess(ParameterExpression parameter, IProperty property)
    {
        return property.PropertyInfo is not null
            ? Expression.Property(parameter, property.PropertyInfo)
            : Expression.Property(parameter, property.Name);
    }

    private static KeyPart CreateKeyPart(
        IProperty property,
        Expression propertyAccess,
        Func<TKey, object?> keyValueGetter)
    {
        var selector = BuildKeyPartSelector(propertyAccess);
        return new KeyPart(property, selector, keyValueGetter);
    }

    private static LambdaExpression BuildKeyPartSelector(Expression propertyAccess)
    {
        var parameter = propertyAccess switch
        {
            MemberExpression memberExpression when memberExpression.Expression is ParameterExpression parameterExpression => parameterExpression,
            _ => throw new InvalidOperationException(
                "Key selector must resolve to a direct member access for sorting and pagination.")
        };

        var delegateType = typeof(Func<,>).MakeGenericType(typeof(TEntity), propertyAccess.Type);
        return Expression.Lambda(delegateType, propertyAccess, parameter);
    }

    private static Func<TCompositeKey, object?> CreateCompositeKeyPartGetter<TCompositeKey>(
        string propertyName)
        where TCompositeKey : notnull
    {
        var keyParameter = Expression.Parameter(typeof(TCompositeKey), "key");
        var property = Expression.Property(keyParameter, propertyName);
        var box = Expression.Convert(property, typeof(object));
        return Expression.Lambda<Func<TCompositeKey, object?>>(box, keyParameter).Compile();
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

    private static Expression BuildEntityKeyPartEqualsExpression(
        ParameterExpression parameter,
        KeyPart keyPart,
        object? keyValue)
    {
        var left = BuildPropertyAccess(parameter, keyPart.Property);
        var right = Expression.Constant(keyValue, keyPart.Property.ClrType);
        return Expression.Equal(left, right);
    }

    private ResolvedKey ResolveKey(Expression<Func<TEntity, TKey>>? keySelector)
    {
        if (keySelector is not null)
        {
            return ResolveExplicitKey(keySelector);
        }

        if (PrimaryKey.Properties.Count == 0)
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' has no primary-key properties configured in the EF Core model.");
        }

        if (PrimaryKey.Properties.Count > 2)
        {
            var propertyNames = string.Join(", ", PrimaryKey.Properties.Select(property => property.Name));
            throw new NotSupportedException(
                $"Entity type '{typeof(TEntity).Name}' has a {PrimaryKey.Properties.Count}-part primary key ({propertyNames}), but RestLib currently supports at most two-part keys.");
        }

        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        if (PrimaryKey.Properties.Count == 1)
        {
            var keyProperty = PrimaryKey.Properties[0];
            if (keyProperty.ClrType != typeof(TKey))
            {
                throw new InvalidOperationException(
                    $"Entity type '{typeof(TEntity).Name}' has primary key property '{keyProperty.Name}' of type '{keyProperty.ClrType.Name}', but the registration specifies TKey as '{typeof(TKey).Name}'.");
            }

            var propertyAccess = BuildPropertyAccess(parameter, keyProperty);
            var resolvedSelector = Expression.Lambda<Func<TEntity, TKey>>(propertyAccess, parameter);

            return new ResolvedKey(
                resolvedSelector.Compile(),
                [CreateKeyPart(keyProperty, propertyAccess, static key => key)]);
        }

        if (!typeof(TKey).IsGenericType
            || typeof(TKey).GetGenericTypeDefinition() != typeof(RestLibCompositeKey<,>))
        {
            var propertyNames = string.Join(", ", PrimaryKey.Properties.Select(property => property.Name));
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' has a composite primary key ({propertyNames}), but the registration specifies TKey '{typeof(TKey).Name}' instead of RestLibCompositeKey<TFirst, TSecond>.");
        }

        var keyArguments = typeof(TKey).GetGenericArguments();
        if (PrimaryKey.Properties[0].ClrType != keyArguments[0]
            || PrimaryKey.Properties[1].ClrType != keyArguments[1])
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).Name}' composite primary key types '{PrimaryKey.Properties[0].ClrType.Name}' and '{PrimaryKey.Properties[1].ClrType.Name}' must match TKey generic arguments '{keyArguments[0].Name}' and '{keyArguments[1].Name}'.");
        }

        var firstAccess = BuildPropertyAccess(parameter, PrimaryKey.Properties[0]);
        var secondAccess = BuildPropertyAccess(parameter, PrimaryKey.Properties[1]);
        var constructor = typeof(TKey).GetConstructor(
            [PrimaryKey.Properties[0].ClrType, PrimaryKey.Properties[1].ClrType])
            ?? throw new InvalidOperationException(
                $"RestLib could not resolve the composite key constructor for '{typeof(TKey).Name}'.");
        var body = Expression.New(constructor, firstAccess, secondAccess);
        var compositeSelector = Expression.Lambda<Func<TEntity, TKey>>(body, parameter);

        return new ResolvedKey(
            compositeSelector.Compile(),
            [
                CreateKeyPart(
                    PrimaryKey.Properties[0],
                    firstAccess,
                    CreateCompositeKeyPartGetter<TKey>(nameof(RestLibCompositeKey<int, int>.First))),
                CreateKeyPart(
                    PrimaryKey.Properties[1],
                    secondAccess,
                    CreateCompositeKeyPartGetter<TKey>(nameof(RestLibCompositeKey<int, int>.Second)))
            ]);
    }

    private ResolvedKey ResolveExplicitKey(Expression<Func<TEntity, TKey>> keySelector)
    {
        if (typeof(TKey).IsGenericType
            && typeof(TKey).GetGenericTypeDefinition() == typeof(RestLibCompositeKey<,>))
        {
            return ResolveExplicitCompositeKey(keySelector);
        }

        var keyProperty = ResolveExplicitKeyProperty(keySelector.Body, typeof(TKey), "KeySelector");
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var propertyAccess = BuildPropertyAccess(parameter, keyProperty);
        var selector = Expression.Lambda<Func<TEntity, TKey>>(propertyAccess, parameter);

        return new ResolvedKey(
            selector.Compile(),
            [CreateKeyPart(keyProperty, propertyAccess, static key => key)]);
    }

    private ResolvedKey ResolveExplicitCompositeKey(Expression<Func<TEntity, TKey>> keySelector)
    {
        var body = UnwrapConvert(keySelector.Body);
        if (body is not NewExpression { Arguments.Count: 2 })
        {
            throw new InvalidOperationException(
                $"The explicit key selector for entity type '{typeof(TEntity).Name}' must create a " +
                $"{nameof(RestLibCompositeKey<int, int>)} value from two direct mapped properties.");
        }

        var newExpression = (NewExpression)body;
        var keyArguments = typeof(TKey).GetGenericArguments();
        var firstProperty = ResolveExplicitKeyProperty(
            newExpression.Arguments[0],
            keyArguments[0],
            "KeySelector");
        var secondProperty = ResolveExplicitKeyProperty(
            newExpression.Arguments[1],
            keyArguments[1],
            "KeySelector");
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var firstAccess = BuildPropertyAccess(parameter, firstProperty);
        var secondAccess = BuildPropertyAccess(parameter, secondProperty);
        var constructor = typeof(TKey).GetConstructor([keyArguments[0], keyArguments[1]])
            ?? throw new InvalidOperationException(
                $"RestLib could not resolve the composite key constructor for '{typeof(TKey).Name}'.");
        var bodyExpression = Expression.New(constructor, firstAccess, secondAccess);
        var compositeSelector = Expression.Lambda<Func<TEntity, TKey>>(bodyExpression, parameter);

        return new ResolvedKey(
            compositeSelector.Compile(),
            [
                CreateKeyPart(
                    firstProperty,
                    firstAccess,
                    CreateCompositeKeyPartGetter<TKey>(nameof(RestLibCompositeKey<int, int>.First))),
                CreateKeyPart(
                    secondProperty,
                    secondAccess,
                    CreateCompositeKeyPartGetter<TKey>(nameof(RestLibCompositeKey<int, int>.Second)))
            ]);
    }

    private IProperty ResolveExplicitKeyProperty(
        Expression expression,
        Type expectedType,
        string optionName)
    {
        var memberExpression = UnwrapConvert(expression) as MemberExpression;
        if (memberExpression?.Member is not PropertyInfo propertyInfo
            || UnwrapConvert(memberExpression.Expression!) is not ParameterExpression)
        {
            throw new InvalidOperationException(
                $"The explicit key selector for entity type '{typeof(TEntity).Name}' must access direct mapped properties only.");
        }

        if (propertyInfo.PropertyType != expectedType)
        {
            throw new InvalidOperationException(
                $"The explicit key selector property '{propertyInfo.Name}' on entity type '{typeof(TEntity).Name}' " +
                $"must be of type '{expectedType.Name}', but it is '{propertyInfo.PropertyType.Name}'.");
        }

        return _entityType.FindProperty(propertyInfo.Name)
            ?? throw new InvalidOperationException(
                $"{nameof(EfCoreRepositoryOptions<TEntity, TKey>)}.{optionName} selects property '{propertyInfo.Name}' " +
                $"on entity type '{typeof(TEntity).Name}', but that property is not mapped in the EF Core model.");
    }

    private sealed record ResolvedKey(
        Func<TEntity, TKey> KeyAccessor,
        IReadOnlyList<KeyPart> KeyParts);

    private sealed record KeyPart(
        IProperty Property,
        LambdaExpression Selector,
        Func<TKey, object?> GetKeyValue)
    {
        internal string PropertyName => Property.Name;
    }
}
