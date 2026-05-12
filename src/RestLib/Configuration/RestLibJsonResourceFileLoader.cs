using System.Text.Json;

namespace RestLib.Configuration;

/// <summary>
/// Loads root-level JSON resource configuration files.
/// </summary>
internal static class RestLibJsonResourceFileLoader
{
    /// <summary>
    /// Loads a single root-level <see cref="RestLibJsonResourceConfiguration"/> from a JSON file.
    /// </summary>
    /// <param name="path">The JSON file path.</param>
    /// <returns>The loaded resource configuration.</returns>
    internal static RestLibJsonResourceConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var resolvedPath = ResolvePath(path);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                $"RestLib JSON resource file was not found: '{path}'. Resolved path: '{resolvedPath}'.",
                resolvedPath);
        }

        try
        {
            var json = File.ReadAllText(resolvedPath);
            var configuration = RestLibJsonResourceConfigurationLoader.LoadFromJson(json);

            ValidateRequiredProperties(configuration, resolvedPath);
            return configuration;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(BuildJsonExceptionMessage(resolvedPath, ex), ex);
        }
    }

    /// <summary>
    /// Resolves a relative or absolute path against the current application base directory.
    /// </summary>
    /// <param name="path">The input path.</param>
    /// <returns>The absolute resolved path.</returns>
    internal static string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        var currentDirectoryPath = Path.GetFullPath(path, Directory.GetCurrentDirectory());
        if (File.Exists(currentDirectoryPath) || Directory.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        var appBaseDirectoryPath = Path.GetFullPath(path, AppContext.BaseDirectory);
        if (File.Exists(appBaseDirectoryPath) || Directory.Exists(appBaseDirectoryPath))
        {
            return appBaseDirectoryPath;
        }

        return currentDirectoryPath;
    }

    private static void ValidateRequiredProperties(RestLibJsonResourceConfiguration configuration, string path)
    {
        if (string.IsNullOrWhiteSpace(configuration.Name) || string.IsNullOrWhiteSpace(configuration.Route))
        {
            throw new InvalidOperationException(
                $"JSON resource file '{path}' does not contain a valid RestLib resource definition. Both 'Name' and 'Route' are required.");
        }
    }

    private static string BuildJsonExceptionMessage(string path, JsonException exception)
    {
        if (exception.LineNumber is not null && exception.BytePositionInLine is not null)
        {
            return $"Failed to parse RestLib JSON resource file '{path}' at line {exception.LineNumber + 1}, column {exception.BytePositionInLine + 1}: {exception.Message}";
        }

        if (exception.BytePositionInLine is not null)
        {
            return $"Failed to parse RestLib JSON resource file '{path}' at byte position {exception.BytePositionInLine}: {exception.Message}";
        }

        return $"Failed to parse RestLib JSON resource file '{path}': {exception.Message}";
    }
}
