using System.Linq.Expressions;
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
    private static readonly Lazy<ReflectionRestLibMapper<TApiModel, TDbModel>> SharedMapper =
        new(static () => new ReflectionRestLibMapper<TApiModel, TDbModel>());
    private static readonly Lazy<MappingPlan<TDbModel, TApiModel>> ToApiMapping =
        new(static () => BuildMapping<TDbModel, TApiModel>());
    private static readonly Lazy<MappingPlan<TApiModel, TDbModel>> ToDbMapping =
        new(static () => BuildMapping<TApiModel, TDbModel>());
    private readonly MappingPlan<TDbModel, TApiModel> _toApi;
    private readonly MappingPlan<TApiModel, TDbModel> _toDb;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReflectionRestLibMapper{TApiModel, TDbModel}"/> class.
    /// </summary>
    public ReflectionRestLibMapper()
    {
        _toApi = ToApiMapping.Value;
        _toDb = ToDbMapping.Value;
    }

    /// <summary>
    /// Gets the shared built-in mapper for this closed model pair.
    /// </summary>
    internal static ReflectionRestLibMapper<TApiModel, TDbModel> Shared => SharedMapper.Value;

    /// <inheritdoc />
    public TApiModel ToApi(TDbModel dbModel)
    {
        ArgumentNullException.ThrowIfNull(dbModel);

        return Map(dbModel, _toApi);
    }

    /// <inheritdoc />
    public TDbModel ToDb(TApiModel apiModel)
    {
        ArgumentNullException.ThrowIfNull(apiModel);

        return Map(apiModel, _toDb);
    }

    private static MappingPlan<TSource, TDestination> BuildMapping<TSource, TDestination>()
        where TSource : class
        where TDestination : class
    {
        ValidateDestinationType(typeof(TDestination));

        var source = Expression.Parameter(typeof(TSource), "source");
        var destination = Expression.Parameter(typeof(TDestination), "destination");
        var assignments = new List<Expression>();
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

            assignments.Add(Expression.Assign(
                Expression.Property(destination, destinationProperty),
                Expression.Property(source, sourceProperty)));
        }

        var constructor = typeof(TDestination).GetConstructor(Type.EmptyTypes)!;
        var factory = Expression.Lambda<Func<TDestination>>(Expression.New(constructor)).Compile();
        Expression copyBody = assignments.Count == 0
            ? Expression.Empty()
            : Expression.Block(assignments);
        var copy = Expression.Lambda<Action<TSource, TDestination>>(
            copyBody,
            source,
            destination).Compile();

        return new MappingPlan<TSource, TDestination>(factory, copy);
    }

    private static TDestination Map<TSource, TDestination>(
        TSource source,
        MappingPlan<TSource, TDestination> plan)
        where TSource : class
        where TDestination : class
    {
        TDestination destination;
        try
        {
            destination = plan.Factory();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Reflection auto mapper could not create destination type '{typeof(TDestination).FullName}'. " +
                "The type must expose a public parameterless constructor.",
                ex);
        }

        plan.Copy(source, destination);
        return destination;
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

    private sealed record MappingPlan<TSource, TDestination>(
        Func<TDestination> Factory,
        Action<TSource, TDestination> Copy)
        where TSource : class
        where TDestination : class;
}
