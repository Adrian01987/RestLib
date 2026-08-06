using System.Globalization;

namespace RestLib.Internal;

/// <summary>
/// Applies RestLib's shared enum parsing and membership contract.
/// </summary>
internal static class EnumValueValidator
{
    /// <summary>
    /// Parses a case-insensitive enum value and verifies that it contains only declared values.
    /// </summary>
    /// <param name="enumType">The enum type.</param>
    /// <param name="value">The value to parse.</param>
    /// <param name="parsedValue">The parsed value when valid.</param>
    /// <returns><c>true</c> when the value is valid; otherwise, <c>false</c>.</returns>
    internal static bool TryParse(Type enumType, string value, out object? parsedValue)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        ArgumentNullException.ThrowIfNull(value);

        if (!enumType.IsEnum)
        {
            throw new ArgumentException($"Type '{enumType.Name}' is not an enum.", nameof(enumType));
        }

        if (!Enum.TryParse(enumType, value, ignoreCase: true, out parsedValue) ||
            parsedValue is null ||
            !IsValid(enumType, parsedValue))
        {
            parsedValue = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether a parsed enum value belongs to the enum's declared value set.
    /// </summary>
    /// <param name="enumType">The enum type.</param>
    /// <param name="value">The parsed enum value.</param>
    /// <returns><c>true</c> when the value is valid; otherwise, <c>false</c>.</returns>
    internal static bool IsValid(Type enumType, object value)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        ArgumentNullException.ThrowIfNull(value);

        if (!enumType.IsEnum || value.GetType() != enumType)
        {
            return false;
        }

        if (Enum.IsDefined(enumType, value))
        {
            return true;
        }

        if (!enumType.IsDefined(typeof(FlagsAttribute), inherit: false))
        {
            return false;
        }

        var declaredBits = 0UL;
        foreach (var declaredValue in Enum.GetValues(enumType))
        {
            declaredBits |= GetUnderlyingBits(enumType, declaredValue);
        }

        var valueBits = GetUnderlyingBits(enumType, value);
        return (valueBits & ~declaredBits) == 0;
    }

    private static ulong GetUnderlyingBits(Type enumType, object value)
    {
        return Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
        {
            TypeCode.SByte => unchecked((byte)Convert.ToSByte(value, CultureInfo.InvariantCulture)),
            TypeCode.Byte => Convert.ToByte(value, CultureInfo.InvariantCulture),
            TypeCode.Int16 => unchecked((ushort)Convert.ToInt16(value, CultureInfo.InvariantCulture)),
            TypeCode.UInt16 => Convert.ToUInt16(value, CultureInfo.InvariantCulture),
            TypeCode.Int32 => unchecked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            TypeCode.UInt32 => Convert.ToUInt32(value, CultureInfo.InvariantCulture),
            TypeCode.Int64 => unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            TypeCode.UInt64 => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Enum '{enumType.Name}' has an unsupported underlying type."),
        };
    }
}
