using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;

namespace RestLib.Mapping;

/// <summary>
/// Resolves the active RestLib mapper for an API/DB model pair.
/// </summary>
internal static class RestLibMapperResolver
{
    /// <summary>
    /// Resolves the mapper for the given API and DB model types.
    /// </summary>
    /// <typeparam name="TApiModel">The API model type.</typeparam>
    /// <typeparam name="TDbModel">The DB model type.</typeparam>
    /// <param name="services">The current service provider.</param>
    /// <param name="mapperName">
    /// Optional mapper name used to select a specific mapper implementation by
    /// type name, full name, or assembly-qualified name.
    /// </param>
    /// <param name="useAutoMapper">
    /// A value indicating whether the built-in strict reflection mapper should
    /// be used.
    /// </param>
    /// <param name="resourceName">
    /// The optional JSON resource name used for clearer startup and request
    /// error messages.
    /// </param>
    /// <returns>The resolved mapper.</returns>
    internal static IRestLibMapper<TApiModel, TDbModel> Resolve<TApiModel, TDbModel>(
        IServiceProvider services,
        string? mapperName = null,
        bool useAutoMapper = false,
        string? resourceName = null)
        where TApiModel : class
        where TDbModel : class
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!string.IsNullOrWhiteSpace(mapperName) && useAutoMapper)
        {
            throw new InvalidOperationException(
                BuildResourcePrefix(resourceName) +
                "cannot configure both a named mapper and the reflection auto mapper.");
        }

        if (useAutoMapper)
        {
            try
            {
                if (typeof(TApiModel) == typeof(TDbModel))
                {
                    return (IRestLibMapper<TApiModel, TDbModel>)(object)new IdentityMapper<TApiModel>();
                }

                return services.GetService<ReflectionRestLibMapper<TApiModel, TDbModel>>()
                    ?? new ReflectionRestLibMapper<TApiModel, TDbModel>();
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    BuildResourcePrefix(resourceName) +
                    $"could not create the reflection auto mapper for API model '{typeof(TApiModel).FullName}' " +
                    $"and DB model '{typeof(TDbModel).FullName}': {ex.Message}",
                    ex);
            }
        }

        if (!string.IsNullOrWhiteSpace(mapperName))
        {
            var matches = services.GetServices<IRestLibMapper<TApiModel, TDbModel>>()
                .Where(mapper => MatchesMapperName(mapper, mapperName))
                .ToList();

            if (matches.Count == 1)
            {
                return matches[0];
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    BuildResourcePrefix(resourceName) +
                    $"configured mapper '{mapperName}' for API model '{typeof(TApiModel).FullName}' and DB model '{typeof(TDbModel).FullName}', " +
                    $"but multiple registered mappers matched that name: {string.Join(", ", matches.Select(mapper => mapper.GetType().FullName))}.");
            }

            throw new InvalidOperationException(
                BuildResourcePrefix(resourceName) +
                $"configured mapper '{mapperName}' for API model '{typeof(TApiModel).FullName}' and DB model '{typeof(TDbModel).FullName}', " +
                "but no registered mapper matched that name.");
        }

        var mapper = services.GetService<IRestLibMapper<TApiModel, TDbModel>>();
        if (mapper is not null)
        {
            return mapper;
        }

        if (typeof(TApiModel) == typeof(TDbModel))
        {
            return (IRestLibMapper<TApiModel, TDbModel>)(object)new IdentityMapper<TApiModel>();
        }

        throw new InvalidOperationException(
            BuildResourcePrefix(resourceName) +
            $"no RestLib mapper is registered for API model '{typeof(TApiModel).FullName}' " +
            $"and DB model '{typeof(TDbModel).FullName}'. " +
            $"Register one with AddRestLibMapper<{typeof(TApiModel).Name}, {typeof(TDbModel).Name}, TMapper>().");
    }

    private static string BuildResourcePrefix(string? resourceName)
    {
        return string.IsNullOrWhiteSpace(resourceName)
            ? string.Empty
            : $"RestLib resource '{resourceName}' ";
    }

    private static bool MatchesMapperName<TApiModel, TDbModel>(
        IRestLibMapper<TApiModel, TDbModel> mapper,
        string mapperName)
        where TApiModel : class
        where TDbModel : class
    {
        var mapperType = mapper.GetType();
        return string.Equals(mapperType.Name, mapperName, StringComparison.Ordinal)
            || string.Equals(mapperType.FullName, mapperName, StringComparison.Ordinal)
            || string.Equals(mapperType.AssemblyQualifiedName, mapperName, StringComparison.Ordinal);
    }
}
