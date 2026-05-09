using System.Reflection;

namespace RestLib.Configuration;

/// <summary>
/// Validates mapped query configuration for two-model resources.
/// </summary>
internal static class MappedQueryConfigurationValidator
{
    /// <summary>
    /// Validates that filter and sort properties configured on the API model are
    /// also available on the DB model with compatible types.
    /// </summary>
    /// <typeparam name="TApiModel">The API model type.</typeparam>
    /// <typeparam name="TDbModel">The DB model type.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="configuration">The mapped endpoint configuration.</param>
    internal static void Validate<TApiModel, TDbModel, TKey>(
        RestLibEndpointConfiguration<TApiModel, TDbModel, TKey> configuration)
        where TApiModel : class
        where TDbModel : class
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (typeof(TApiModel) == typeof(TDbModel))
        {
            return;
        }

        foreach (var filter in configuration.FilterConfiguration.Properties)
        {
            ValidateProperty<TApiModel, TDbModel>(filter.PropertyName, filter.PropertyType, "filtering");
        }

        foreach (var sort in configuration.SortConfiguration.Properties)
        {
            ValidateProperty<TApiModel, TDbModel>(sort.PropertyName, sort.PropertyType, "sorting");
        }
    }

    private static void ValidateProperty<TApiModel, TDbModel>(
        string propertyName,
        Type apiPropertyType,
        string usage)
        where TApiModel : class
        where TDbModel : class
    {
        var dbProperty = typeof(TDbModel).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance);

        if (dbProperty is null)
        {
            throw new InvalidOperationException(
                $"Mapped {usage} property '{propertyName}' is configured on API model " +
                $"'{typeof(TApiModel).FullName}' but does not exist on DB model '{typeof(TDbModel).FullName}'.");
        }

        if (dbProperty.PropertyType != apiPropertyType)
        {
            throw new InvalidOperationException(
                $"Mapped {usage} property '{propertyName}' is configured on API model " +
                $"'{typeof(TApiModel).FullName}' with CLR type '{apiPropertyType.FullName}', but DB model " +
                $"'{typeof(TDbModel).FullName}' exposes '{dbProperty.PropertyType.FullName}'. " +
                "Sprint 002 requires the same CLR property name and type for mapped filtering and sorting.");
        }
    }
}
