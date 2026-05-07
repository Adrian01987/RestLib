using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using RestLib.Configuration;

namespace RestLib.Validation;

/// <summary>
/// Validates entities against JSON-declared validation rules.
/// </summary>
internal static class JsonValidationRuleValidator
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    /// <summary>
    /// Validates an entity against JSON-declared rules.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity to validate.</param>
    /// <param name="rules">The rules keyed by CLR property name.</param>
    /// <param name="namingPolicy">The JSON naming policy for error field names.</param>
    /// <returns>The validation result.</returns>
    internal static EntityValidationResult Validate<TEntity>(
        TEntity entity,
        IReadOnlyDictionary<string, RestLibJsonValidationRuleConfiguration> rules,
        JsonNamingPolicy? namingPolicy)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Count == 0)
        {
            return EntityValidationResult.Success();
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in rules)
        {
            var property = typeof(TEntity).GetProperty(entry.Key, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    $"Property '{entry.Key}' was not found on entity type '{typeof(TEntity).Name}'.");

            var propertyValue = property.GetValue(entity);
            var fieldName = namingPolicy?.ConvertName(property.Name) ?? property.Name;
            ValidateProperty(property, propertyValue, fieldName, entry.Value, errors);
        }

        if (errors.Count == 0)
        {
            return EntityValidationResult.Success();
        }

        return EntityValidationResult.Failed(
            errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
    }

    private static void ValidateProperty(
        PropertyInfo property,
        object? value,
        string fieldName,
        RestLibJsonValidationRuleConfiguration rules,
        IDictionary<string, List<string>> errors)
    {
        if (rules.Required && IsRequiredFailure(value))
        {
            AddError(errors, fieldName, $"The {property.Name} field is required.");
        }

        if (value is null)
        {
            return;
        }

        if (rules.Min is not null && TryConvertToDecimal(value, out var minComparable) && minComparable < rules.Min.Value)
        {
            AddError(errors, fieldName, $"The {property.Name} field must be greater than or equal to {FormatDecimal(rules.Min.Value)}.");
        }

        if (rules.Max is not null && TryConvertToDecimal(value, out var maxComparable) && maxComparable > rules.Max.Value)
        {
            AddError(errors, fieldName, $"The {property.Name} field must be less than or equal to {FormatDecimal(rules.Max.Value)}.");
        }

        if (value is string stringValue)
        {
            if (rules.Length?.Min is int minLength && stringValue.Length < minLength)
            {
                AddError(errors, fieldName, $"The {property.Name} field must be at least {minLength} characters long.");
            }

            if (rules.Length?.Max is int maxLength && stringValue.Length > maxLength)
            {
                AddError(errors, fieldName, $"The {property.Name} field must be at most {maxLength} characters long.");
            }

            if (!string.IsNullOrWhiteSpace(rules.Pattern) && !Regex.IsMatch(stringValue, rules.Pattern, RegexOptions.CultureInvariant))
            {
                AddError(errors, fieldName, $"The {property.Name} field is not in the correct format.");
            }

            if (rules.Email && !EmailValidator.IsValid(stringValue))
            {
                AddError(errors, fieldName, $"The {property.Name} field is not a valid email address.");
            }
        }
    }

    private static bool IsRequiredFailure(object? value)
    {
        return value switch
        {
            null => true,
            string stringValue => string.IsNullOrWhiteSpace(stringValue),
            _ => false
        };
    }

    private static bool TryConvertToDecimal(object value, out decimal converted)
    {
        switch (value)
        {
            case byte byteValue:
                converted = byteValue;
                return true;
            case sbyte sbyteValue:
                converted = sbyteValue;
                return true;
            case short shortValue:
                converted = shortValue;
                return true;
            case ushort ushortValue:
                converted = ushortValue;
                return true;
            case int intValue:
                converted = intValue;
                return true;
            case uint uintValue:
                converted = uintValue;
                return true;
            case long longValue:
                converted = longValue;
                return true;
            case ulong ulongValue:
                converted = ulongValue;
                return true;
            case float floatValue:
                converted = (decimal)floatValue;
                return true;
            case double doubleValue:
                converted = (decimal)doubleValue;
                return true;
            case decimal decimalValue:
                converted = decimalValue;
                return true;
            default:
                converted = default;
                return false;
        }
    }

    private static string FormatDecimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static void AddError(IDictionary<string, List<string>> errors, string fieldName, string message)
    {
        if (!errors.TryGetValue(fieldName, out var fieldErrors))
        {
            fieldErrors = new List<string>();
            errors[fieldName] = fieldErrors;
        }

        fieldErrors.Add(message);
    }
}
