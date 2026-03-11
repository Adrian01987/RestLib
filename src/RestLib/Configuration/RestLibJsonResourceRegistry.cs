using Microsoft.AspNetCore.Routing;

namespace RestLib.Configuration;

/// <summary>
/// Stores typed JSON resource registrations that can later be mapped to endpoints.
/// </summary>
public sealed class RestLibJsonResourceRegistry
{
  private readonly Dictionary<string, Action<IEndpointRouteBuilder>> _registrations =
      new(StringComparer.OrdinalIgnoreCase);

  /// <summary>
  /// Maps all registered JSON resources to the provided endpoint builder.
  /// </summary>
  /// <param name="endpoints">The endpoint route builder.</param>
  public void MapAll(IEndpointRouteBuilder endpoints)
  {
    ArgumentNullException.ThrowIfNull(endpoints);

    foreach (var registration in _registrations.Values)
    {
      registration(endpoints);
    }
  }

  /// <summary>
  /// Maps a single named JSON resource to the provided endpoint builder.
  /// </summary>
  /// <param name="endpoints">The endpoint route builder.</param>
  /// <param name="name">The resource name.</param>
  public void Map(IEndpointRouteBuilder endpoints, string name)
  {
    ArgumentNullException.ThrowIfNull(endpoints);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);

    if (_registrations.TryGetValue(name, out var registration))
    {
      registration(endpoints);
      return;
    }

    throw new InvalidOperationException($"No RestLib JSON resource named '{name}' has been registered.");
  }

  internal void Add(string name, Action<IEndpointRouteBuilder> mapAction)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(mapAction);

    if (_registrations.ContainsKey(name))
    {
      throw new InvalidOperationException($"A RestLib JSON resource named '{name}' is already registered.");
    }

    _registrations[name] = mapAction;
  }
}
