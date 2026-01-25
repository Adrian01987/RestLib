using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace RestLib.Validation;

/// <summary>
/// Provides entity validation using Data Annotations.
/// </summary>
public static class EntityValidator
{
  /// <summary>
  /// Validates an entity using Data Annotations.
  /// </summary>
  /// <typeparam name="TEntity">The entity type.</typeparam>
  /// <param name="entity">The entity to validate.</param>
  /// <param name="namingPolicy">The JSON naming policy for error field names.</param>
  /// <returns>A validation result containing any errors.</returns>
  public static EntityValidationResult Validate<TEntity>(
      TEntity entity,
      JsonNamingPolicy? namingPolicy = null)
      where TEntity : class
  {
    ArgumentNullException.ThrowIfNull(entity);

    var validationResults = new List<ValidationResult>();
    var validationContext = new ValidationContext(entity);

    var isValid = Validator.TryValidateObject(
        entity,
        validationContext,
        validationResults,
        validateAllProperties: true);

    if (isValid)
    {
      return EntityValidationResult.Success();
    }

    // Convert to dictionary with snake_case field names
    var errors = ConvertToErrorDictionary(validationResults, namingPolicy);

    return EntityValidationResult.Failed(errors);
  }

  /// <summary>
  /// Converts validation results to a dictionary with properly cased field names.
  /// </summary>
  private static Dictionary<string, string[]> ConvertToErrorDictionary(
      List<ValidationResult> validationResults,
      JsonNamingPolicy? namingPolicy)
  {
    var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    foreach (var result in validationResults)
    {
      var message = result.ErrorMessage ?? "Validation failed.";
      var memberNames = result.MemberNames.Any()
          ? result.MemberNames
          : new[] { string.Empty };

      foreach (var memberName in memberNames)
      {
        // Convert property name to the configured naming policy (snake_case by default)
        var fieldName = string.IsNullOrEmpty(memberName)
            ? "_entity"
            : ConvertFieldName(memberName, namingPolicy);

        if (errors.TryGetValue(fieldName, out var existingErrors))
        {
          errors[fieldName] = existingErrors.Concat(new[] { message }).ToArray();
        }
        else
        {
          errors[fieldName] = new[] { message };
        }
      }
    }

    return errors;
  }

  /// <summary>
  /// Converts a property name to the configured naming convention.
  /// </summary>
  private static string ConvertFieldName(string propertyName, JsonNamingPolicy? namingPolicy)
  {
    if (namingPolicy is null)
    {
      return propertyName;
    }

    return namingPolicy.ConvertName(propertyName);
  }
}
