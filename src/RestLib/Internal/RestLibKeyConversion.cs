using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace RestLib.Internal;

/// <summary>
/// Shared conversion helpers for route, log, and JSON key values.
/// </summary>
internal static class RestLibKeyConversion
{
    /// <summary>
    /// Formats a key value for logs and error messages using invariant culture.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted value.</returns>
    internal static string FormatDisplayValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    /// <summary>
    /// Formats a key value for use as a URL path segment.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The escaped route segment.</returns>
    internal static string FormatRouteSegment(object? value)
    {
        return Uri.EscapeDataString(FormatDisplayValue(value));
    }

    /// <summary>
    /// Converts a required route value to the requested CLR type.
    /// </summary>
    /// <typeparam name="T">The target CLR type.</typeparam>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="routeParameterName">The route parameter name.</param>
    /// <returns>The converted route value.</returns>
    internal static T ConvertRouteValue<T>(HttpContext httpContext, string routeParameterName)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeParameterName);

        if (!httpContext.Request.RouteValues.TryGetValue(routeParameterName, out var rawValue)
            || rawValue is null)
        {
            throw new BadHttpRequestException(
                $"The required route value '{routeParameterName}' was not provided.");
        }

        var stringValue = rawValue as string
            ?? Convert.ToString(rawValue, CultureInfo.InvariantCulture)
            ?? string.Empty;

        try
        {
            return (T)ConvertString(stringValue, typeof(T));
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or NotSupportedException)
        {
            throw new BadHttpRequestException(
                $"The route value '{routeParameterName}' is not valid for type '{typeof(T).Name}'.",
                ex);
        }
    }

    /// <summary>
    /// Deserializes a JSON value into the requested CLR type.
    /// </summary>
    /// <param name="value">The JSON value.</param>
    /// <param name="targetType">The target CLR type.</param>
    /// <param name="jsonOptions">The JSON serializer options.</param>
    /// <returns>The deserialized value.</returns>
    internal static object DeserializeJsonValue(
        JsonElement value,
        Type targetType,
        JsonSerializerOptions? jsonOptions = null)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        return JsonSerializer.Deserialize(value.GetRawText(), targetType, jsonOptions)
            ?? throw new InvalidOperationException(
                $"RestLib could not deserialize a composite key value to '{targetType.Name}'.");
    }

    /// <summary>
    /// Converts a string representation to the requested CLR type.
    /// </summary>
    /// <param name="value">The source string value.</param>
    /// <param name="targetType">The target CLR type.</param>
    /// <returns>The converted value.</returns>
    private static object ConvertString(string value, Type targetType)
    {
        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (effectiveType == typeof(string))
        {
            return value;
        }

        if (effectiveType.IsEnum)
        {
            return Enum.Parse(effectiveType, value, ignoreCase: true);
        }

        var converter = TypeDescriptor.GetConverter(effectiveType);
        if (!converter.CanConvertFrom(typeof(string)))
        {
            throw new NotSupportedException(
                $"Type '{effectiveType.Name}' does not support conversion from route strings.");
        }

        return converter.ConvertFrom(null, CultureInfo.InvariantCulture, value)
            ?? throw new InvalidOperationException(
                $"RestLib could not convert route value '{value}' to '{effectiveType.Name}'.");
    }
}
