using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace RestLib.Configuration;

/// <summary>
/// Loads <see cref="RestLibJsonResourceConfiguration"/> values from JSON elements and
/// configuration sections while preserving RestLib's additive compatibility rules.
/// </summary>
internal static class RestLibJsonResourceConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    static RestLibJsonResourceConfigurationLoader()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    /// <summary>
    /// Loads a resource configuration from a JSON string.
    /// </summary>
    /// <param name="json">The raw JSON content.</param>
    /// <returns>The loaded resource configuration.</returns>
    internal static RestLibJsonResourceConfiguration LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        return LoadFromJsonElement(document.RootElement);
    }

    /// <summary>
    /// Loads a resource configuration from a parsed JSON element.
    /// </summary>
    /// <param name="element">The parsed JSON element.</param>
    /// <returns>The loaded resource configuration.</returns>
    internal static RestLibJsonResourceConfiguration LoadFromJsonElement(JsonElement element)
    {
        var sanitizedJson = BuildSanitizedJson(element);
        var configuration = JsonSerializer.Deserialize<RestLibJsonResourceConfiguration>(sanitizedJson, JsonOptions)
            ?? throw new InvalidOperationException("The JSON resource definition could not be loaded.");

        if (TryGetPropertyCaseInsensitive(element, "FieldSelection", out var fieldSelectionElement))
        {
            ApplyFieldSelectionElement(configuration, fieldSelectionElement);
        }

        return configuration;
    }

    /// <summary>
    /// Loads a resource configuration from an <see cref="IConfigurationSection"/>.
    /// </summary>
    /// <param name="section">The configuration section.</param>
    /// <returns>The loaded resource configuration.</returns>
    internal static RestLibJsonResourceConfiguration LoadFromConfigurationSection(IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var configuration = section.Get<RestLibJsonResourceConfiguration>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{section.Path}' does not contain a valid RestLib resource definition.");

        ApplyFieldSelectionSection(configuration, section.GetSection("FieldSelection"));
        return configuration;
    }

    private static void ApplyFieldSelectionElement(
        RestLibJsonResourceConfiguration configuration,
        JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                configuration.FieldSelection = JsonSerializer.Deserialize<List<string>>(element.GetRawText(), JsonOptions) ?? [];
                configuration.FieldSelectionResponse = null;
                return;
            case JsonValueKind.Object:
                configuration.FieldSelection = TryGetPropertyCaseInsensitive(element, "Properties", out var propertiesElement)
                    ? JsonSerializer.Deserialize<List<string>>(propertiesElement.GetRawText(), JsonOptions) ?? []
                    : [];

                configuration.FieldSelectionResponse = TryGetPropertyCaseInsensitive(element, "Response", out var responseElement)
                    ? responseElement.GetString()
                    : null;
                return;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                configuration.FieldSelection = [];
                configuration.FieldSelectionResponse = null;
                return;
            default:
                throw new InvalidOperationException(
                    "FieldSelection must be either an array of property names or an object with 'Properties' and optional 'Response'.");
        }
    }

    private static void ApplyFieldSelectionSection(
        RestLibJsonResourceConfiguration configuration,
        IConfigurationSection fieldSelectionSection)
    {
        if (!fieldSelectionSection.Exists())
        {
            return;
        }

        var children = fieldSelectionSection.GetChildren().ToList();
        if (children.Count == 0)
        {
            return;
        }

        var looksLikeArray = children.All(child => int.TryParse(child.Key, out _));
        if (looksLikeArray)
        {
            configuration.FieldSelection = fieldSelectionSection.Get<List<string>>() ?? [];
            configuration.FieldSelectionResponse = null;
            return;
        }

        configuration.FieldSelection = fieldSelectionSection.GetSection("Properties").Get<List<string>>() ?? [];
        configuration.FieldSelectionResponse = fieldSelectionSection.GetValue<string>("Response");
    }

    private static string BuildSanitizedJson(JsonElement element)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, "FieldSelection", StringComparison.OrdinalIgnoreCase))
            {
                writer.WritePropertyName(property.Name);

                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Array:
                        property.Value.WriteTo(writer);
                        break;
                    case JsonValueKind.Object:
                        if (TryGetPropertyCaseInsensitive(property.Value, "Properties", out var propertiesElement))
                        {
                            propertiesElement.WriteTo(writer);
                        }
                        else
                        {
                            writer.WriteStartArray();
                            writer.WriteEndArray();
                        }

                        break;
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        writer.WriteStartArray();
                        writer.WriteEndArray();
                        break;
                    default:
                        throw new InvalidOperationException(
                            "FieldSelection must be either an array of property names or an object with 'Properties' and optional 'Response'.");
                }

                continue;
            }

            writer.WritePropertyName(property.Name);
            property.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
