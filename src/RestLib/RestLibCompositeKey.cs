using System.Reflection;
using Microsoft.AspNetCore.Http;
using RestLib.Configuration;
using RestLib.Internal;

namespace RestLib;

/// <summary>
/// Represents an ordered two-part resource key.
/// </summary>
/// <typeparam name="TFirst">The first key-part type.</typeparam>
/// <typeparam name="TSecond">The second key-part type.</typeparam>
public readonly record struct RestLibCompositeKey<TFirst, TSecond>(TFirst First, TSecond Second)
    where TFirst : notnull
    where TSecond : notnull
{
    /// <inheritdoc />
    public override string ToString()
    {
        return $"First='{RestLibKeyConversion.FormatDisplayValue(First)}', Second='{RestLibKeyConversion.FormatDisplayValue(Second)}'";
    }

    /// <summary>
    /// Binds the composite key from route values for Minimal API endpoints.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="parameter">The target endpoint parameter.</param>
    /// <returns>The bound composite key.</returns>
    public static ValueTask<RestLibCompositeKey<TFirst, TSecond>?> BindAsync(
        HttpContext httpContext,
        ParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(parameter);

        var metadata = httpContext.GetEndpoint()?.Metadata.GetMetadata<RestLibCompositeKeyBindingMetadata>()
            ?? throw new BadHttpRequestException(
                $"RestLib composite key binding metadata was not found for parameter '{parameter.Name}'.");

        var first = RestLibKeyConversion.ConvertRouteValue<TFirst>(httpContext, metadata.FirstRouteParameter);
        var second = RestLibKeyConversion.ConvertRouteValue<TSecond>(httpContext, metadata.SecondRouteParameter);

        return ValueTask.FromResult<RestLibCompositeKey<TFirst, TSecond>?>(new RestLibCompositeKey<TFirst, TSecond>(first, second));
    }
}
