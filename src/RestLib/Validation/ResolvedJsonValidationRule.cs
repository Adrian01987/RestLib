using System.Text.RegularExpressions;

namespace RestLib.Validation;

/// <summary>
/// Immutable runtime representation of a JSON-declared validation rule.
/// </summary>
internal sealed class ResolvedJsonValidationRule
{
    /// <summary>
    /// The maximum amount of time a configured regular expression may execute.
    /// </summary>
    internal const int PatternMatchTimeoutMilliseconds = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolvedJsonValidationRule"/> class.
    /// </summary>
    /// <param name="required">Whether the property is required.</param>
    /// <param name="min">The minimum numeric value.</param>
    /// <param name="max">The maximum numeric value.</param>
    /// <param name="minLength">The minimum string length.</param>
    /// <param name="maxLength">The maximum string length.</param>
    /// <param name="pattern">The compiled regular expression.</param>
    /// <param name="email">Whether the property must contain an email address.</param>
    internal ResolvedJsonValidationRule(
        bool required,
        decimal? min,
        decimal? max,
        int? minLength,
        int? maxLength,
        Regex? pattern,
        bool email)
    {
        Required = required;
        Min = min;
        Max = max;
        MinLength = minLength;
        MaxLength = maxLength;
        Pattern = pattern;
        Email = email;
    }

    /// <summary>
    /// Gets the configured regular-expression match timeout.
    /// </summary>
    internal static TimeSpan PatternMatchTimeout { get; } =
        TimeSpan.FromMilliseconds(PatternMatchTimeoutMilliseconds);

    /// <summary>
    /// Gets a value indicating whether the property is required.
    /// </summary>
    internal bool Required { get; }

    /// <summary>
    /// Gets the minimum numeric value.
    /// </summary>
    internal decimal? Min { get; }

    /// <summary>
    /// Gets the maximum numeric value.
    /// </summary>
    internal decimal? Max { get; }

    /// <summary>
    /// Gets the minimum string length.
    /// </summary>
    internal int? MinLength { get; }

    /// <summary>
    /// Gets the maximum string length.
    /// </summary>
    internal int? MaxLength { get; }

    /// <summary>
    /// Gets the compiled regular expression, when configured.
    /// </summary>
    internal Regex? Pattern { get; }

    /// <summary>
    /// Gets a value indicating whether the property must contain an email address.
    /// </summary>
    internal bool Email { get; }
}
