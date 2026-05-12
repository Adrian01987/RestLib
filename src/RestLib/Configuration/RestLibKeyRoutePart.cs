namespace RestLib.Configuration;

/// <summary>
/// Describes one ordered route segment that participates in a resource key.
/// </summary>
/// <typeparam name="TKey">The full key type.</typeparam>
internal sealed record RestLibKeyRoutePart<TKey>(
    string PropertyName,
    string RouteParameterName,
    Type PropertyType,
    Func<TKey, object?> GetKeyPartValue)
    where TKey : notnull;

/// <summary>
/// Endpoint metadata used by composite-key route binding.
/// </summary>
internal sealed record RestLibCompositeKeyBindingMetadata(
    string FirstRouteParameter,
    string SecondRouteParameter);
