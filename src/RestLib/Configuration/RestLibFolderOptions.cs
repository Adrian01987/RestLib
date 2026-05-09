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
    public Func<string, RestLibJsonResourceConfiguration, (Type EntityType, Type KeyType)>? TypeResolver { get; set; }
}
