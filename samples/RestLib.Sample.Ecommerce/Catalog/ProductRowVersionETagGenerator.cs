using System.Text.Json;
using RestLib.Abstractions;
using RestLib.Caching;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Catalog;

/// <summary>
/// Generates product ETags from the EF Core row-version token.
/// </summary>
public sealed class ProductRowVersionETagGenerator : IETagGenerator
{
    private readonly HashBasedETagGenerator _fallback;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductRowVersionETagGenerator"/> class.
    /// </summary>
    /// <param name="jsonOptions">The RestLib JSON serializer options.</param>
    public ProductRowVersionETagGenerator(JsonSerializerOptions jsonOptions)
    {
        _fallback = new HashBasedETagGenerator(jsonOptions);
    }

    /// <inheritdoc />
    public string Generate<TEntity>(TEntity entity)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is Product product)
        {
            var token = product.RowVersion.Length == 0
                ? product.Id.ToByteArray()
                : product.RowVersion;

            return $"\"{EncodeToBase64Url(token)}\"";
        }

        return _fallback.Generate(entity);
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

        var normalized = etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
            ? etag[2..]
            : etag;

        return string.Equals(Generate(entity), normalized, StringComparison.Ordinal);
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
