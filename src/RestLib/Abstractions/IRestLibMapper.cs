namespace RestLib.Abstractions;

/// <summary>
/// Maps between the API model exposed at the HTTP boundary and the DB model
/// persisted through RestLib repositories.
/// </summary>
/// <typeparam name="TApiModel">The API model type.</typeparam>
/// <typeparam name="TDbModel">The DB model type.</typeparam>
public interface IRestLibMapper<TApiModel, TDbModel>
    where TApiModel : class
    where TDbModel : class
{
    /// <summary>
    /// Maps a DB model to its API representation.
    /// </summary>
    /// <param name="dbModel">The DB model instance.</param>
    /// <returns>The mapped API model.</returns>
    TApiModel ToApi(TDbModel dbModel);

    /// <summary>
    /// Maps an API model to its DB representation.
    /// </summary>
    /// <param name="apiModel">The API model instance.</param>
    /// <returns>The mapped DB model.</returns>
    TDbModel ToDb(TApiModel apiModel);
}
