using System.Reflection;

namespace RestLib.Configuration;

/// <summary>
/// Options for loading RestLib JSON resource files from a folder.
/// </summary>
public sealed class RestLibFolderOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RestLibFolderOptions"/> class.
    /// </summary>
    public RestLibFolderOptions()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is not null)
        {
            Assemblies.Add(entryAssembly);
        }
    }

    /// <summary>
    /// Gets the assemblies searched by the default type resolver when a resource file does not declare EntityType.
    /// </summary>
    public IList<Assembly> Assemblies { get; } = new List<Assembly>();

    /// <summary>
    /// Gets or sets a delegate that resolves API-model and key CLR types for a
    /// resource file.
    /// </summary>
    /// <remarks>
    /// When <see cref="UnifiedTypeResolver"/> is set, it takes precedence over this
    /// legacy resolver.
    /// </remarks>
    public Func<string, RestLibJsonResourceConfiguration, (Type EntityType, Type KeyType)>? TypeResolver { get; set; }

    /// <summary>
    /// Gets or sets a delegate that resolves API, DB, and key CLR types for a
    /// resource file.
    /// </summary>
    /// <remarks>
    /// When set, this resolver takes precedence over <see cref="TypeResolver"/> and
    /// over JSON <c>Mapping.DbType</c> resolution. Return <c>null</c> to fall back to
    /// the legacy resolver flow.
    /// </remarks>
    public Func<string, RestLibJsonResourceConfiguration, RestLibResolvedResourceTypes?>? UnifiedTypeResolver { get; set; }
}
