using System.Reflection;
using RestLib.Abstractions;

namespace RestLib.Mapping;

/// <summary>
/// Strict reflection-based mapper used for JSON auto-mapping.
/// </summary>
/// <typeparam name="TApiModel">The API model type.</typeparam>
/// <typeparam name="TDbModel">The DB model type.</typeparam>
internal sealed class ReflectionRestLibMapper<TApiModel, TDbModel> : IRestLibMapper<TApiModel, TDbModel>
    where TApiModel : class
    where TDbModel : class
{
    private readonly IReadOnlyList<PropertyMapping> _toApiMappings;
    private readonly IReadOnlyList<PropertyMapping> _toDbMappings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReflectionRestLibMapper{TApiModel, TDbModel}"/> class.
    /// </summary>
    public ReflectionRestLibMapper()
    {
        _toApiMappings = BuildMappings<TDbModel, TApiModel>();
        _toDbMappings = BuildMappings<TApiModel, TDbModel>();
    }

    /// <inheritdoc />
    public TApiModel ToApi(TDbModel dbModel)
    {
        ArgumentNullException.ThrowIfNull(dbModel);

        var apiModel = CreateInstance<TApiModel>();
        CopyProperties(dbModel, apiModel, _toApiMappings);
        return apiModel;
    }

    /// <inheritdoc />
    public TDbModel ToDb(TApiModel apiModel)
    {
        ArgumentNullException.ThrowIfNull(apiModel);

        var dbModel = CreateInstance<TDbModel>();
        CopyProperties(apiModel, dbModel, _toDbMappings);
        return dbModel;
    }

    private static IReadOnlyList<PropertyMapping> BuildMappings<TSource, TDestination>()
        where TSource : class
        where TDestination : class
    {
        ValidateDestinationType(typeof(TDestination));

        var mappings = new List<PropertyMapping>();
        var destinationProperties = typeof(TDestination)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => property.SetMethod is not null && property.SetMethod.IsPublic)
            .OrderBy(property => property.Name, StringComparer.Ordinal);

        foreach (var destinationProperty in destinationProperties)
        {
            var sourceProperty = typeof(TSource).GetProperty(destinationProperty.Name, BindingFlags.Public | BindingFlags.Instance);
            if (sourceProperty is null || sourceProperty.GetMethod is null || !sourceProperty.GetMethod.IsPublic)
            {
                throw new InvalidOperationException(
                    $"Reflection auto mapper from '{typeof(TSource).FullName}' to '{typeof(TDestination).FullName}' " +
                    $"requires destination property '{destinationProperty.Name}' to have a readable source property with the same CLR type.");
            }

            if (sourceProperty.PropertyType != destinationProperty.PropertyType)
            {
                throw new InvalidOperationException(
                    $"Reflection auto mapper from '{typeof(TSource).FullName}' to '{typeof(TDestination).FullName}' " +
                    $"requires destination property '{destinationProperty.Name}' to exactly match the source CLR type. " +
                    $"Source type: '{sourceProperty.PropertyType.FullName}'. Destination type: '{destinationProperty.PropertyType.FullName}'.");
            }

            mappings.Add(new PropertyMapping(sourceProperty, destinationProperty));
        }

        return mappings;
    }

    private static void ValidateDestinationType(Type destinationType)
    {
        if (!destinationType.IsClass || destinationType.IsAbstract)
        {
            throw new InvalidOperationException(
                $"Reflection auto mapper requires destination type '{destinationType.FullName}' to be a non-abstract class.");
        }

        if (destinationType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Reflection auto mapper requires destination type '{destinationType.FullName}' to expose a public parameterless constructor.");
        }
    }

    private static TDestination CreateInstance<TDestination>()
        where TDestination : class
    {
        try
        {
            return Activator.CreateInstance<TDestination>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Reflection auto mapper could not create destination type '{typeof(TDestination).FullName}'. " +
                "The type must expose a public parameterless constructor.",
                ex);
        }
    }

    private static void CopyProperties<TSource, TDestination>(
        TSource source,
        TDestination destination,
        IReadOnlyList<PropertyMapping> mappings)
        where TSource : class
        where TDestination : class
    {
        foreach (var mapping in mappings)
        {
            var value = mapping.Source.GetValue(source);
            mapping.Destination.SetValue(destination, value);
        }
    }

    private readonly record struct PropertyMapping(PropertyInfo Source, PropertyInfo Destination);
}
