using System.Text.Json;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Catalog;

/// <summary>
/// Generates ETags from app-managed row-version tokens in the ecommerce sample.
/// </summary>
public sealed class EcommerceRowVersionETagGenerator : IETagGenerator
{
    private readonly HashBasedETagGenerator _fallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="EcommerceRowVersionETagGenerator"/> class.
    /// </summary>
    /// <param name="jsonOptions">The RestLib JSON serializer options.</param>
    public EcommerceRowVersionETagGenerator(JsonSerializerOptions jsonOptions)
    {
        _fallback = new HashBasedETagGenerator(jsonOptions);
    }

    /// <inheritdoc />
    public string Generate<TEntity>(TEntity entity)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        return entity switch
        {
            Product product => GenerateFromRowVersion(product.RowVersion, product.Id),
            Order order => GenerateFromRowVersion(order.RowVersion, order.Id),
            _ => _fallback.Generate(entity),
        };
    }

    /// <inheritdoc />
    public bool Validate<TEntity>(TEntity entity, string etag)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (string.IsNullOrWhiteSpace(etag))
        {
            return false;
        }

        if (etag == "*")
        {
            return true;
        }

        var generated = Generate(entity);
        var normalized = etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
            ? etag[2..]
            : etag;

        return string.Equals(generated, normalized, StringComparison.Ordinal);
    }

    private static string GenerateFromRowVersion(byte[] rowVersion, Guid id)
    {
        var tokenBytes = rowVersion.Length > 0 ? rowVersion : id.ToByteArray();
        return $"\"{EncodeToBase64Url(tokenBytes)}\"";
    }

    private static string EncodeToBase64Url(byte[] bytes)
    {
        var base64 = Convert.ToBase64String(bytes);
        var length = base64.AsSpan().TrimEnd('=').Length;
        return string.Create(length, (base64, length), static (span, state) =>
        {
            state.base64.AsSpan(0, state.length).CopyTo(span);
            span.Replace('+', '-');
            span.Replace('/', '_');
        });
    }
}
