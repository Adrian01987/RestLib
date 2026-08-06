using RestLib.Abstractions;

namespace RestLib.Endpoints;

/// <summary>
/// Defines the representation boundary used by endpoint state machines.
/// </summary>
/// <typeparam name="TApiModel">The API representation type.</typeparam>
/// <typeparam name="TDbModel">The persistence model type.</typeparam>
internal sealed class EndpointModelAdapter<TApiModel, TDbModel>
    where TApiModel : class
    where TDbModel : class
{
    private readonly IRestLibMapper<TApiModel, TDbModel> _mapper;

    private EndpointModelAdapter(
        IRestLibMapper<TApiModel, TDbModel> mapper,
        bool isIdentity)
    {
        _mapper = mapper;
        IsIdentity = isIdentity;
    }

    /// <summary>
    /// Gets a value indicating whether the API and persistence representations are identical.
    /// </summary>
    internal bool IsIdentity { get; }

    /// <summary>
    /// Gets the mapper used by conditional ETag operations.
    /// </summary>
    internal IRestLibMapper<TApiModel, TDbModel> Mapper => _mapper;

    /// <summary>
    /// Creates the identity boundary used by one-model resources.
    /// </summary>
    /// <typeparam name="TModel">The shared API and persistence model type.</typeparam>
    /// <returns>An identity model adapter.</returns>
    internal static EndpointModelAdapter<TModel, TModel> Identity<TModel>()
        where TModel : class =>
        new(new IdentityRestLibMapper<TModel>(), isIdentity: true);

    /// <summary>
    /// Creates a mapped boundary used by two-model resources.
    /// </summary>
    /// <param name="mapper">The configured representation mapper.</param>
    /// <returns>A mapped model adapter.</returns>
    internal static EndpointModelAdapter<TApiModel, TDbModel> Mapped(
        IRestLibMapper<TApiModel, TDbModel> mapper) =>
        new(mapper, isIdentity: false);

    /// <summary>
    /// Maps a persistence model to its API representation.
    /// </summary>
    /// <param name="dbModel">The persistence model.</param>
    /// <returns>The API representation.</returns>
    internal TApiModel ToApi(TDbModel dbModel) => _mapper.ToApi(dbModel);

    /// <summary>
    /// Maps an API representation to its persistence model.
    /// </summary>
    /// <param name="apiModel">The API representation.</param>
    /// <returns>The persistence model.</returns>
    internal TDbModel ToDb(TApiModel apiModel) => _mapper.ToDb(apiModel);

    private sealed class IdentityRestLibMapper<TModel> : IRestLibMapper<TModel, TModel>
        where TModel : class
    {
        public TModel ToApi(TModel dbModel) => dbModel;

        public TModel ToDb(TModel apiModel) => apiModel;
    }
}

/// <summary>
/// Holds the current API and persistence representations while an endpoint state machine runs.
/// </summary>
/// <typeparam name="TApiModel">The API representation type.</typeparam>
/// <typeparam name="TDbModel">The persistence model type.</typeparam>
internal sealed class EndpointModelState<TApiModel, TDbModel>
    where TApiModel : class
    where TDbModel : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointModelState{TApiModel, TDbModel}"/> class.
    /// </summary>
    /// <param name="apiModel">The current API representation.</param>
    /// <param name="dbModel">The current persistence model.</param>
    internal EndpointModelState(TApiModel apiModel, TDbModel dbModel)
    {
        ApiModel = apiModel;
        DbModel = dbModel;
    }

    /// <summary>
    /// Gets or sets the current API representation.
    /// </summary>
    internal TApiModel ApiModel { get; set; }

    /// <summary>
    /// Gets or sets the current persistence model.
    /// </summary>
    internal TDbModel DbModel { get; set; }
}

/// <summary>
/// Holds optional API and persistence representations for operations that do not always load an entity.
/// </summary>
/// <typeparam name="TApiModel">The API representation type.</typeparam>
/// <typeparam name="TDbModel">The persistence model type.</typeparam>
internal sealed class OptionalEndpointModelState<TApiModel, TDbModel>
    where TApiModel : class
    where TDbModel : class
{
    /// <summary>
    /// Gets or sets the current API representation, when one has been loaded.
    /// </summary>
    internal TApiModel? ApiModel { get; set; }

    /// <summary>
    /// Gets or sets the current persistence model, when one has been loaded.
    /// </summary>
    internal TDbModel? DbModel { get; set; }
}
