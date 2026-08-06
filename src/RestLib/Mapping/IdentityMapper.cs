using RestLib.Abstractions;

namespace RestLib.Mapping;

/// <summary>
/// Identity mapper used for single-model RestLib resources.
/// </summary>
/// <typeparam name="TModel">The shared API and DB model type.</typeparam>
public sealed class IdentityMapper<TModel> : IRestLibMapper<TModel, TModel>
    where TModel : class
{
    /// <summary>
    /// Gets the shared stateless identity mapper used by RestLib endpoint pipelines.
    /// </summary>
    internal static IdentityMapper<TModel> Shared { get; } = new();

    /// <summary>
    /// Returns the same model instance as the API representation.
    /// </summary>
    /// <param name="dbModel">The DB model instance.</param>
    /// <returns>The same instance.</returns>
    public TModel ToApi(TModel dbModel) => dbModel;

    /// <summary>
    /// Returns the same model instance as the DB representation.
    /// </summary>
    /// <param name="apiModel">The API model instance.</param>
    /// <returns>The same instance.</returns>
    public TModel ToDb(TModel apiModel) => apiModel;
}
