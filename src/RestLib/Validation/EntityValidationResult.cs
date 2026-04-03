namespace RestLib.Validation;

/// <summary>
/// Represents the result of entity validation.
/// </summary>
public class EntityValidationResult
{
  private EntityValidationResult(bool isValid, IReadOnlyDictionary<string, string[]>? errors = null)
  {
    IsValid = isValid;
    Errors = errors ?? new Dictionary<string, string[]>();
  }

  /// <summary>
  /// Gets whether the validation was successful.
  /// </summary>
  public bool IsValid { get; }

  /// <summary>
  /// Gets the validation errors keyed by field name (in configured naming convention).
  /// </summary>
  public IReadOnlyDictionary<string, string[]> Errors { get; }

  /// <summary>
  /// Creates a successful validation result.
  /// </summary>
  public static EntityValidationResult Success()
  {
    return new EntityValidationResult(true);
  }

  /// <summary>
  /// Creates a failed validation result with the specified errors.
  /// </summary>
  /// <param name="errors">Dictionary of field names to error messages.</param>
  public static EntityValidationResult Failed(IDictionary<string, string[]> errors)
  {
    return new EntityValidationResult(false, new Dictionary<string, string[]>(errors));
  }
}
