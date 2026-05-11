using System.Linq.Expressions;
using RestLib.Internal;

namespace RestLib.Configuration;

/// <summary>
/// Fluent configuration extensions for two-part composite keys.
/// </summary>
public static class RestLibCompositeKeyConfigurationExtensions
{
    /// <summary>
     /// Configures an ordered two-part composite key and route template for the resource.
     /// </summary>
    /// <typeparam name="TEntity">The resource model type.</typeparam>
    /// <typeparam name="TFirst">The first key-part type.</typeparam>
    /// <typeparam name="TSecond">The second key-part type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="firstKey">The first key property selector.</param>
    /// <param name="firstRouteParameter">The first route parameter name.</param>
    /// <param name="secondKey">The second key property selector.</param>
    /// <param name="secondRouteParameter">The second route parameter name.</param>
    /// <returns>The same configuration instance.</returns>
    public static RestLibEndpointConfiguration<TEntity, RestLibCompositeKey<TFirst, TSecond>> UseCompositeKey<TEntity, TFirst, TSecond>(
        this RestLibEndpointConfiguration<TEntity, RestLibCompositeKey<TFirst, TSecond>> config,
        Expression<Func<TEntity, TFirst>> firstKey,
        string firstRouteParameter,
        Expression<Func<TEntity, TSecond>> secondKey,
        string secondRouteParameter)
        where TEntity : class
        where TFirst : notnull
        where TSecond : notnull
    {
        return UseCompositeKey<
            RestLibEndpointConfiguration<TEntity, RestLibCompositeKey<TFirst, TSecond>>,
            TEntity,
            TFirst,
            TSecond>(
            config,
            firstKey,
            firstRouteParameter,
            secondKey,
            secondRouteParameter);
    }

    /// <summary>
    /// Configures an ordered two-part composite key and route template for a mapped resource.
    /// </summary>
    /// <typeparam name="TApiModel">The API model type.</typeparam>
    /// <typeparam name="TDbModel">The DB model type.</typeparam>
    /// <typeparam name="TFirst">The first key-part type.</typeparam>
    /// <typeparam name="TSecond">The second key-part type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="firstKey">The first key property selector.</param>
    /// <param name="firstRouteParameter">The first route parameter name.</param>
    /// <param name="secondKey">The second key property selector.</param>
    /// <param name="secondRouteParameter">The second route parameter name.</param>
    /// <returns>The same configuration instance.</returns>
    public static RestLibEndpointConfiguration<TApiModel, TDbModel, RestLibCompositeKey<TFirst, TSecond>> UseCompositeKey<TApiModel, TDbModel, TFirst, TSecond>(
        this RestLibEndpointConfiguration<TApiModel, TDbModel, RestLibCompositeKey<TFirst, TSecond>> config,
        Expression<Func<TApiModel, TFirst>> firstKey,
        string firstRouteParameter,
        Expression<Func<TApiModel, TSecond>> secondKey,
        string secondRouteParameter)
        where TApiModel : class
        where TDbModel : class
        where TFirst : notnull
        where TSecond : notnull
    {
        _ = UseCompositeKey<
            RestLibEndpointConfiguration<TApiModel, TDbModel, RestLibCompositeKey<TFirst, TSecond>>,
            TApiModel,
            TFirst,
            TSecond>(
            config,
            firstKey,
            firstRouteParameter,
            secondKey,
            secondRouteParameter);

        return config;
    }

    /// <summary>
    /// Configures an ordered two-part composite key and route template for the resource.
    /// </summary>
    /// <typeparam name="TConfiguration">The endpoint configuration type.</typeparam>
    /// <typeparam name="TEntity">The resource model type.</typeparam>
    /// <typeparam name="TFirst">The first key-part type.</typeparam>
    /// <typeparam name="TSecond">The second key-part type.</typeparam>
    /// <param name="config">The endpoint configuration.</param>
    /// <param name="firstKey">The first key property selector.</param>
    /// <param name="firstRouteParameter">The first route parameter name.</param>
    /// <param name="secondKey">The second key property selector.</param>
    /// <param name="secondRouteParameter">The second route parameter name.</param>
    /// <returns>The same configuration instance.</returns>
    public static TConfiguration UseCompositeKey<TConfiguration, TEntity, TFirst, TSecond>(
        this TConfiguration config,
        Expression<Func<TEntity, TFirst>> firstKey,
        string firstRouteParameter,
        Expression<Func<TEntity, TSecond>> secondKey,
        string secondRouteParameter)
        where TConfiguration : RestLibEndpointConfiguration<TEntity, RestLibCompositeKey<TFirst, TSecond>>
        where TEntity : class
        where TFirst : notnull
        where TSecond : notnull
    {
        ArgumentNullException.ThrowIfNull(config);

        var firstProperty = NamingUtils.GetDirectProperty(firstKey, nameof(firstKey));
        var secondProperty = NamingUtils.GetDirectProperty(secondKey, nameof(secondKey));

        ValidateRouteParameterName(firstRouteParameter, nameof(firstRouteParameter));
        ValidateRouteParameterName(secondRouteParameter, nameof(secondRouteParameter));

        if (string.Equals(firstRouteParameter, secondRouteParameter, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Composite key route parameter names must be unique.",
                nameof(secondRouteParameter));
        }

        var firstGetter = firstKey.Compile();
        var secondGetter = secondKey.Compile();

        config.KeySelector = entity => new RestLibCompositeKey<TFirst, TSecond>(
            firstGetter(entity),
            secondGetter(entity));
        config.SetKeyRouteParts(
        [
            new RestLibKeyRoutePart<RestLibCompositeKey<TFirst, TSecond>>(
                firstProperty.Name,
                firstRouteParameter,
                typeof(TFirst),
                static key => key.First),
            new RestLibKeyRoutePart<RestLibCompositeKey<TFirst, TSecond>>(
                secondProperty.Name,
                secondRouteParameter,
                typeof(TSecond),
                static key => key.Second)
        ]);

        return config;
    }

    private static void ValidateRouteParameterName(string routeParameterName, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeParameterName, parameterName);

        if (!char.IsLetter(routeParameterName[0]) && routeParameterName[0] != '_')
        {
            throw new ArgumentException(
                "Route parameter names must start with a letter or underscore.",
                parameterName);
        }

        if (routeParameterName.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
        {
            throw new ArgumentException(
                "Route parameter names may only contain letters, digits, or underscores.",
                parameterName);
        }
    }
}
