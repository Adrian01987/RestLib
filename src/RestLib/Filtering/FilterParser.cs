using System.ComponentModel;
using Microsoft.AspNetCore.Http;

namespace RestLib.Filtering;

/// <summary>
/// Parses and validates filter query parameters.
/// </summary>
public static class FilterParser
{
  /// <summary>
  /// Parses filter values from a query collection based on configured filter properties.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <param name="query">The query collection from the request.</param>
  /// <param name="configuration">The filter configuration.</param>
  /// <returns>A parse result containing values and any errors.</returns>
  public static FilterParseResult Parse<TEntity>(
      IQueryCollection query,
      FilterConfiguration<TEntity> configuration)
      where TEntity : class
  {
    var values = new List<FilterValue>();
    var errors = new List<FilterValidationError>();

    foreach (var property in configuration.Properties)
    {
      if (!query.TryGetValue(property.QueryParameterName, out var rawValues))
      {
        continue;
      }

      foreach (var rawValue in rawValues)
      {
        if (string.IsNullOrEmpty(rawValue))
        {
          continue;
        }

        var (success, typedValue, errorMessage) = TryConvertValue(rawValue, property.PropertyType);

        if (success)
        {
          values.Add(new FilterValue
          {
            PropertyName = property.PropertyName,
            QueryParameterName = property.QueryParameterName,
            PropertyType = property.PropertyType,
            RawValue = rawValue,
            TypedValue = typedValue
          });
        }
        else
        {
          errors.Add(new FilterValidationError
          {
            ParameterName = property.QueryParameterName,
            ProvidedValue = rawValue,
            ExpectedType = property.PropertyType,
            Message = errorMessage ?? $"Cannot convert '{rawValue}' to {GetFriendlyTypeName(property.PropertyType)}"
          });
        }
      }
    }

    return new FilterParseResult
    {
      Values = values,
      Errors = errors
    };
  }

  /// <summary>
  /// Attempts to convert a string value to the target type.
  /// </summary>
  private static (bool Success, object? Value, string? ErrorMessage) TryConvertValue(string rawValue, Type targetType)
  {
    try
    {
      // Handle nullable types
      var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

      // Special handling for common types
      if (underlyingType == typeof(string))
      {
        return (true, rawValue, null);
      }

      if (underlyingType == typeof(bool))
      {
        // Accept common boolean representations
        if (bool.TryParse(rawValue, out var boolValue))
        {
          return (true, boolValue, null);
        }
        // Handle "1" and "0" as boolean
        if (rawValue == "1")
        {
          return (true, true, null);
        }
        if (rawValue == "0")
        {
          return (true, false, null);
        }
        return (false, null, $"'{rawValue}' is not a valid boolean. Use 'true', 'false', '1', or '0'.");
      }

      if (underlyingType == typeof(Guid))
      {
        if (Guid.TryParse(rawValue, out var guidValue))
        {
          return (true, guidValue, null);
        }
        return (false, null, $"'{rawValue}' is not a valid GUID.");
      }

      if (underlyingType == typeof(DateTime))
      {
        if (DateTime.TryParse(rawValue, out var dateValue))
        {
          return (true, dateValue, null);
        }
        return (false, null, $"'{rawValue}' is not a valid date/time.");
      }

      if (underlyingType == typeof(DateTimeOffset))
      {
        if (DateTimeOffset.TryParse(rawValue, out var dateValue))
        {
          return (true, dateValue, null);
        }
        return (false, null, $"'{rawValue}' is not a valid date/time.");
      }

      if (underlyingType.IsEnum)
      {
        if (Enum.TryParse(underlyingType, rawValue, ignoreCase: true, out var enumValue))
        {
          return (true, enumValue, null);
        }
        var validValues = string.Join(", ", Enum.GetNames(underlyingType));
        return (false, null, $"'{rawValue}' is not a valid value. Valid values are: {validValues}.");
      }

      // Use TypeConverter for other types (int, long, decimal, etc.)
      var converter = TypeDescriptor.GetConverter(underlyingType);
      if (converter.CanConvertFrom(typeof(string)))
      {
        var converted = converter.ConvertFromString(rawValue);
        if (converted is null)
        {
          return (false, null, $"Cannot convert '{rawValue}' to {GetFriendlyTypeName(targetType)}.");
        }

        return (true, converted, null);
      }

      return (false, null, $"Cannot convert to {GetFriendlyTypeName(targetType)}.");
    }
    catch (Exception ex)
    {
      return (false, null, ex.Message);
    }
  }

  /// <summary>
  /// Gets a user-friendly type name for error messages.
  /// </summary>
  private static string GetFriendlyTypeName(Type type)
  {
    var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

    return underlyingType.Name switch
    {
      "Int32" => "integer",
      "Int64" => "long integer",
      "Decimal" => "decimal number",
      "Double" => "number",
      "Single" => "number",
      "Boolean" => "boolean (true/false)",
      "Guid" => "GUID",
      "DateTime" => "date/time",
      "DateTimeOffset" => "date/time with timezone",
      _ => underlyingType.Name.ToLowerInvariant()
    };
  }
}
