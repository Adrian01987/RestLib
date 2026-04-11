using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RestLib.Logging;

namespace RestLib.Filtering;

/// <summary>
/// Parses and validates filter query parameters.
/// Supports bracket syntax for operators (e.g., <c>?price[gte]=10</c>)
/// and bare equality (e.g., <c>?price=42</c>).
/// </summary>
public static partial class FilterParser
{
    /// <summary>
    /// Default maximum number of values allowed in an <c>in</c> operator list.
    /// </summary>
    internal const int DefaultMaxInListSize = 50;

    /// <summary>
    /// Maps bracket operator strings to <see cref="FilterOperator"/> values.
    /// </summary>
    private static readonly Dictionary<string, FilterOperator> OperatorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eq"] = FilterOperator.Eq,
        ["neq"] = FilterOperator.Neq,
        ["gt"] = FilterOperator.Gt,
        ["lt"] = FilterOperator.Lt,
        ["gte"] = FilterOperator.Gte,
        ["lte"] = FilterOperator.Lte,
        ["contains"] = FilterOperator.Contains,
        ["starts_with"] = FilterOperator.StartsWith,
        ["ends_with"] = FilterOperator.EndsWith,
        ["in"] = FilterOperator.In,
    };

    /// <summary>
    /// Operators that require <see cref="IComparable"/> property types.
    /// </summary>
    private static readonly HashSet<FilterOperator> ComparisonOperators =
    [
        FilterOperator.Gt,
        FilterOperator.Lt,
        FilterOperator.Gte,
        FilterOperator.Lte,
    ];

    /// <summary>
    /// Operators that require <see cref="string"/> property types.
    /// </summary>
    private static readonly HashSet<FilterOperator> StringOperators =
    [
        FilterOperator.Contains,
        FilterOperator.StartsWith,
        FilterOperator.EndsWith,
    ];

    /// <summary>
    /// Parses filter values from a query collection based on configured filter properties.
    /// Supports bracket operator syntax: <c>?field[op]=value</c> and bare equality: <c>?field=value</c>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="query">The query collection from the request.</param>
    /// <param name="configuration">The filter configuration.</param>
    /// <param name="maxInListSize">
    /// Maximum number of values allowed in an <c>in</c> operator list.
    /// Defaults to <see cref="DefaultMaxInListSize"/>.
    /// </param>
    /// <returns>A parse result containing values and any errors.</returns>
    public static FilterParseResult Parse<TEntity>(
        IQueryCollection query,
        FilterConfiguration<TEntity> configuration,
        int maxInListSize = DefaultMaxInListSize)
        where TEntity : class
    {
        var values = new List<FilterValue>();
        var errors = new List<FilterValidationError>();

        // Build a lookup from query parameter name -> property config for fast matching.
        var propertyLookup = new Dictionary<string, FilterPropertyConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in configuration.Properties)
        {
            propertyLookup[prop.QueryParameterName] = prop;
        }

        // Track which (property, operator) pairs we've seen to detect duplicates.
        var seen = new HashSet<(string PropertyName, FilterOperator Op)>();

        foreach (var key in query.Keys)
        {
            var (paramName, operatorStr) = ParseQueryParameterKey(key);

            if (!propertyLookup.TryGetValue(paramName, out var property))
            {
                continue; // Not a configured filter property — silently ignore.
            }

            // Resolve operator
            FilterOperator filterOp;
            if (operatorStr is null)
            {
                filterOp = FilterOperator.Eq;
            }
            else if (!OperatorMap.TryGetValue(operatorStr, out filterOp))
            {
                errors.Add(new FilterValidationError
                {
                    ParameterName = key,
                    ProvidedValue = operatorStr,
                    ExpectedType = property.PropertyType,
                    Message = $"Unknown filter operator '{operatorStr}'. " +
                              $"Valid operators are: {string.Join(", ", OperatorMap.Keys)}.",
                });
                continue;
            }

            // Validate operator is allowed for this property
            if (!property.AllowedOperators.Contains(filterOp))
            {
                var allowedNames = string.Join(", ", property.AllowedOperators
                    .Select(GetOperatorName)
                    .OrderBy(n => n, StringComparer.Ordinal));
                errors.Add(new FilterValidationError
                {
                    ParameterName = key,
                    ProvidedValue = query[key].ToString(),
                    ExpectedType = property.PropertyType,
                    Message = $"Operator '{GetOperatorName(filterOp)}' is not allowed for filter '{property.QueryParameterName}'. " +
                              $"Allowed operators: {allowedNames}.",
                });
                continue;
            }

            // Validate operator/type compatibility
            var typeError = ValidateOperatorTypeCompatibility(filterOp, property, key);
            if (typeError is not null)
            {
                errors.Add(typeError);
                continue;
            }

            // Get raw values — avoid LINQ to reduce allocations on the hot path.
            var rawValues = query[key];
            string? firstNonEmpty = null;
            var nonEmptyCount = 0;

            foreach (var v in rawValues)
            {
                if (!string.IsNullOrEmpty(v))
                {
                    firstNonEmpty ??= v;
                    nonEmptyCount++;
                }
            }

            if (nonEmptyCount == 0)
            {
                continue;
            }

            // Reject multiple values for the same query parameter key
            if (nonEmptyCount > 1)
            {
                errors.Add(new FilterValidationError
                {
                    ParameterName = key,
                    ProvidedValue = string.Join(", ", rawValues.Where(v => !string.IsNullOrEmpty(v))),
                    ExpectedType = property.PropertyType,
                    Message = $"Multiple values for filter '{key}' are not supported. Provide a single value.",
                });
                continue;
            }

            var rawValue = firstNonEmpty!;

            // Detect duplicate (property, operator) pairs
            if (!seen.Add((property.PropertyName, filterOp)))
            {
                errors.Add(new FilterValidationError
                {
                    ParameterName = key,
                    ProvidedValue = rawValue,
                    ExpectedType = property.PropertyType,
                    Message = $"Duplicate filter: '{property.QueryParameterName}' with operator '{GetOperatorName(filterOp)}' was specified more than once.",
                });
                continue;
            }

            // Parse the value
            if (filterOp == FilterOperator.In)
            {
                ParseInOperatorValue(property, key, rawValue, filterOp, values, errors, maxInListSize);
            }
            else
            {
                ParseSingleValue(property, key, rawValue, filterOp, values, errors);
            }
        }

        return new FilterParseResult
        {
            Filters = values,
            Errors = errors,
        };
    }

    /// <summary>
    /// Parses a query parameter key into its base name and optional operator.
    /// For example: <c>"price[gte]"</c> returns <c>("price", "gte")</c>,
    /// and <c>"price"</c> returns <c>("price", null)</c>.
    /// </summary>
    internal static (string ParamName, string? OperatorStr) ParseQueryParameterKey(string key)
    {
        var match = BracketRegex().Match(key);
        if (match.Success)
        {
            return (match.Groups["name"].Value, match.Groups["op"].Value);
        }

        return (key, null);
    }

    /// <summary>
    /// Gets the query-string name for a <see cref="FilterOperator"/>.
    /// </summary>
    internal static string GetOperatorName(FilterOperator op)
    {
        return op switch
        {
            FilterOperator.Eq => "eq",
            FilterOperator.Neq => "neq",
            FilterOperator.Gt => "gt",
            FilterOperator.Lt => "lt",
            FilterOperator.Gte => "gte",
            FilterOperator.Lte => "lte",
            FilterOperator.Contains => "contains",
            FilterOperator.StartsWith => "starts_with",
            FilterOperator.EndsWith => "ends_with",
            FilterOperator.In => "in",
            _ => op.ToString().ToLowerInvariant(),
        };
    }

    /// <summary>
    /// Attempts to convert a string value to the target type.
    /// </summary>
    internal static (bool Success, object? Value, string? ErrorMessage) TryConvertValue(
        string rawValue, Type targetType, ILogger? logger = null, string? parameterName = null)
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
                if (DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateValue))
                {
                    return (true, dateValue, null);
                }

                return (false, null, $"'{rawValue}' is not a valid date/time.");
            }

            if (underlyingType == typeof(DateTimeOffset))
            {
                if (DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateValue))
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
        catch (Exception conversionException)
        {
            if (logger is not null)
            {
                var targetTypeName = GetFriendlyTypeName(targetType);
                RestLibLogMessages.FilterTypeConversionFailed(
                    logger, parameterName ?? "(unknown)", rawValue, targetTypeName, conversionException);
            }

            return (false, null, $"Cannot convert '{rawValue}' to {GetFriendlyTypeName(targetType)}.");
        }
    }

    /// <summary>
    /// Gets a user-friendly type name for error messages.
    /// </summary>
    internal static string GetFriendlyTypeName(Type type)
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
            _ => underlyingType.Name.ToLowerInvariant(),
        };
    }

    [GeneratedRegex(@"^(?<name>[a-z][a-z0-9_]*)\[(?<op>[a-z_]+)\]$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BracketRegex();

    private static FilterValidationError? ValidateOperatorTypeCompatibility(
        FilterOperator op,
        FilterPropertyConfiguration property,
        string queryKey)
    {
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (ComparisonOperators.Contains(op))
        {
            // Comparison operators require IComparable
            if (!typeof(IComparable).IsAssignableFrom(underlyingType))
            {
                return new FilterValidationError
                {
                    ParameterName = queryKey,
                    ProvidedValue = string.Empty,
                    ExpectedType = property.PropertyType,
                    Message = $"Operator '{GetOperatorName(op)}' requires a comparable type, " +
                              $"but '{property.QueryParameterName}' is of type {GetFriendlyTypeName(property.PropertyType)}.",
                };
            }
        }

        if (StringOperators.Contains(op) && underlyingType != typeof(string))
        {
            return new FilterValidationError
            {
                ParameterName = queryKey,
                ProvidedValue = string.Empty,
                ExpectedType = property.PropertyType,
                Message = $"Operator '{GetOperatorName(op)}' is only valid for string properties, " +
                          $"but '{property.QueryParameterName}' is of type {GetFriendlyTypeName(property.PropertyType)}.",
            };
        }

        return null;
    }

    private static void ParseSingleValue(
        FilterPropertyConfiguration property,
        string queryKey,
        string rawValue,
        FilterOperator filterOp,
        List<FilterValue> values,
        List<FilterValidationError> errors)
    {
        var (success, typedValue, errorMessage) = TryConvertValue(rawValue, property.PropertyType);

        if (success)
        {
            values.Add(new FilterValue
            {
                PropertyName = property.PropertyName,
                QueryParameterName = property.QueryParameterName,
                PropertyType = property.PropertyType,
                RawValue = rawValue,
                TypedValue = typedValue,
                Operator = filterOp,
            });
        }
        else
        {
            errors.Add(new FilterValidationError
            {
                ParameterName = queryKey,
                ProvidedValue = rawValue,
                ExpectedType = property.PropertyType,
                Message = errorMessage ?? $"Cannot convert '{rawValue}' to {GetFriendlyTypeName(property.PropertyType)}",
            });
        }
    }

    private static void ParseInOperatorValue(
        FilterPropertyConfiguration property,
        string queryKey,
        string rawValue,
        FilterOperator filterOp,
        List<FilterValue> values,
        List<FilterValidationError> errors,
        int maxInListSize)
    {
        var parts = rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            errors.Add(new FilterValidationError
            {
                ParameterName = queryKey,
                ProvidedValue = rawValue,
                ExpectedType = property.PropertyType,
                Message = $"The 'in' filter for '{property.QueryParameterName}' must contain at least one value.",
            });
            return;
        }

        if (parts.Length > maxInListSize)
        {
            errors.Add(new FilterValidationError
            {
                ParameterName = queryKey,
                ProvidedValue = $"{parts.Length} values",
                ExpectedType = property.PropertyType,
                Message = $"The 'in' filter for '{property.QueryParameterName}' contains {parts.Length} values, " +
                          $"but the maximum is {maxInListSize}.",
            });
            return;
        }

        var typedValues = new List<object?>(parts.Length);
        foreach (var part in parts)
        {
            var (success, typedValue, errorMessage) = TryConvertValue(part, property.PropertyType);
            if (!success)
            {
                errors.Add(new FilterValidationError
                {
                    ParameterName = queryKey,
                    ProvidedValue = part,
                    ExpectedType = property.PropertyType,
                    Message = errorMessage ?? $"Cannot convert '{part}' to {GetFriendlyTypeName(property.PropertyType)}",
                });
                return;
            }

            typedValues.Add(typedValue);
        }

        values.Add(new FilterValue
        {
            PropertyName = property.PropertyName,
            QueryParameterName = property.QueryParameterName,
            PropertyType = property.PropertyType,
            RawValue = rawValue,
            TypedValue = null,
            Operator = filterOp,
            TypedValues = typedValues,
        });
    }
}
