# Query Features

RestLib query features let clients shape collection and item responses without custom
query parsing code.

## See also

- [README](../../README.md)
- [JSON resources guide](json-resources.md)
- [ADR-007: Hybrid field projection strategy](../adr/007-field-selection.md)
- [ADR-009: Allow-list sorting with default sort](../adr/009-sorting.md)
- [ADR-011: Query parameter filtering](../adr/011-filtering.md)
- [ADR-013: Filter operators beyond equality](../adr/013-filter-operators.md)
- [ADR-025: Two-part composite key support](../adr/025-composite-key-support.md)

## Advanced Filtering

Enable query-string filtering with no custom parser code:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowFiltering(p => p.CategoryId, p => p.IsActive);
    config.AllowFiltering(p => p.Price, FilterOperators.Comparison);
    config.AllowFiltering(p => p.Name, FilterOperators.String);
});
```

Equality filters use direct query parameters:

```text
GET /api/products?category_id=5&is_active=true
```

Operator filters use bracket syntax for ranges, partial matches, and set membership:

```text
GET /api/products?price[gte]=20&price[lte]=100
GET /api/products?name[contains]=widget
GET /api/products?status[in]=active,pending
GET /api/orders?customer.email[contains]=example.com
```

Ten operators are available: `eq`, `neq`, `gt`, `lt`, `gte`, `lte`, `contains`,
`starts_with`, `ends_with`, and `in`. Each property declares which operators it supports via
preset arrays (`FilterOperators.Comparison`, `FilterOperators.String`,
`FilterOperators.All`) or individual `FilterOperator` values. `Eq` is always
implicitly allowed.

Relational operators use the common built-in-adapter baseline: `byte`, `short`, `int`,
`long`, `float`, `double`, `decimal`, and `DateTime`, including nullable forms. Other
types can still use equality, inequality, and membership filters. An unsupported
operator/type combination returns 400 before repository query or count execution.

Filter operands use invariant conversion regardless of the server locale. Use `.` in
numeric values (for example, `1234.5`) and ISO-8601 date/time values. Enum names are
case-insensitive; numeric values must identify a declared member. `[Flags]` combinations
may contain only declared bits. Undefined and overflowing values return 400, and every
element of an `in` list follows the same rules.

Partial-string operands are literal and case-insensitive. For example,
`?name[contains]=%25_sale` searches for the text `%_sale`; `%` and `_` are not SQL
wildcards. Null strings do not match, and null numeric/date values do not satisfy a
relational operator.

## Sorting

Control result ordering with an allow-list of sortable properties:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowSorting(p => p.Price, p => p.Name);
    config.DefaultSort("name:asc");
});
```

```text
GET /api/products?sort=price:asc,name:desc&limit=20
```

Sort fields use snake_case names and support `asc`/`desc` directions.
Nested reference-property paths use snake_case per segment joined with dots,
for example `Customer.Name` becomes `customer.name`.
Disallowed fields return a 400 Problem Details response.

Cursor payloads are adapter-specific and must be treated as opaque. EF Core uses keyset
pagination only when every effective sort member is a direct, non-nullable string, number,
GUID, or date with a supported relational comparison. Nullable, enum, Boolean, nested, and
other unsupported sorts automatically use offset cursors. Malformed payloads, negative offsets,
wrong sort signatures, null values, and incorrectly typed values return 400 Invalid Cursor.

## Field Selection

Return only the fields your client needs with sparse fieldsets:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowFieldSelection(p => p.Id, p => p.Name, p => p.Price, p => p.CategoryId);
});
```

```http
GET /api/products?fields=id,name,price
```

Only the selected fields are included in the response. Unknown or disallowed
fields return a 400 Problem Details response. If no `fields` parameter is sent,
the full entity is returned.

Nested reference-property selections are also supported. Query names use
snake_case per segment joined with dots. By default, nested sparse responses use
dotted keys instead of rebuilding nested JSON objects:

```http
GET /api/orders?fields=order_number,customer.email
```

```json
{
  "order_number": "A-100",
  "customer.email": "adam@example.com"
}
```

### Nested object responses (opt-in)

If you prefer rebuilt nested objects for sparse field selection, opt in on the
field-selection configuration:

```csharp
app.MapRestLib<Order, Guid>("/api/orders", config =>
{
    config.AllowFieldSelection(fields =>
    {
        fields.UseNestedObjectsInResponse();
        fields.AddProperty(order => order.OrderNumber);
        fields.AddProperty(order => order.Customer!.Email);
    });
});
```

The same opt-in is available in JSON resources:

```json
{
  "Name": "orders",
  "Route": "/api/orders",
  "FieldSelection": {
    "Properties": ["OrderNumber", "Customer.Email"],
    "Response": "Nested"
  }
}
```

With that opt-in, nested selections render as nested objects:

```json
{
  "order_number": "A-100",
  "customer": {
    "email": "adam@example.com"
  }
}
```

The default remains flat dotted keys for backward compatibility. Once a resource opts
into `Nested`, that shape is stable across sparse selections, dense selections, and
converter-backed projection; internal projection optimizations do not alter the schema.

Field selection works with both GetAll (collection) and GetById (single entity)
endpoints, and combines with filtering, sorting, and pagination.

## Collection Search

Resources can opt into simple collection search that performs an OR-of-contains
match across configured string properties:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowAnonymous();
    config.AllowSearch(p => p.Name, p => p.Description);
});
```

Use `?q=widget` by default, or customize the parameter name and case sensitivity:

```csharp
app.MapRestLib<Product, Guid>("/api/products", config =>
{
    config.AllowAnonymous();
    config.AllowSearch(options =>
    {
        options.QueryParameterName = "query";
        options.CaseSensitive = true;
    }, p => p.Name, p => p.Description);
});
```

JSON resources support the same feature:

```json
{
  "Name": "products",
  "Route": "/api/products",
  "Search": ["Name", "Description"],
  "SearchOptions": {
    "QueryParameter": "query",
    "CaseSensitive": false
  }
}
```

Search is intentionally limited to OR-of-contains matching across configured string
fields. Search terms are literal, so SQL wildcard characters have no special meaning.
The default mode folds case; `CaseSensitive = true` disables RestLib's case folding.

InMemory matching uses ordinal .NET comparisons. EF Core evaluates matching in the
database: Unicode case mapping, accents, and case-sensitive mode ultimately follow the
provider and configured database/column collation. In particular, disabling case folding
cannot force case-sensitive results from a case-insensitive database collation. Query-side
case folding can also affect index use, so inspect query plans for large searchable datasets.
RestLib does not switch these queries to client-side evaluation.

Search is not full-text indexing, ranking, fuzzy matching, or a search engine.

For trivial same-name, same-type models only, JSON can use the built-in strict reflection mapper instead:

```json
"Mapping": {
  "DbType": "ProductEntity, MyApi",
  "Auto": true
}
```

`Auto` does not support renamed properties, type conversions, nested mapping, or computed values. Use a C# mapper for anything beyond direct property copying.

## Composite Keys

RestLib supports ordered two-part composite keys through `RestLibCompositeKey<TFirst, TSecond>`.

Fluent registration:

```csharp
var keyComparer = Comparer<RestLibCompositeKey<Guid, string>>.Create(static (left, right) =>
{
    var tenantComparison = left.First.CompareTo(right.First);
    return tenantComparison != 0
        ? tenantComparison
        : StringComparer.Ordinal.Compare(left.Second, right.Second);
});

builder.Services.AddRestLibInMemory<TenantProduct, RestLibCompositeKey<Guid, string>>(
    p => new RestLibCompositeKey<Guid, string>(p.TenantId, p.Sku),
    () => new RestLibCompositeKey<Guid, string>(Guid.NewGuid(), $"generated-{Guid.NewGuid():N}"),
    (product, key) =>
    {
        product.TenantId = key.First;
        product.Sku = key.Second;
        return product;
    },
    keyComparer);

app.MapRestLib<TenantProduct, RestLibCompositeKey<Guid, string>>("/api/tenant-products", config =>
{
    config.AllowAnonymous();
    config.UseCompositeKey(p => p.TenantId, "tenantId", p => p.Sku, "sku");
});
```

The third delegate explicitly writes generated composite keys back to the entity.
Simple resources with one writable key property, or a conventional `Id` property,
do not need this delegate. Calculated, composite, or otherwise ambiguous generated
keys must provide it so the returned entity and repository storage key cannot diverge.
The comparer supplies the total key order used for default collection ordering and sort
tie-breaking; provide one whenever a composite or custom `TKey` has no natural comparer.

That produces item routes like:

```text
GET /api/tenant-products/{tenantId}/{sku}
```

Composite route segments use invariant conversion. Enum key parts must be declared values
or valid combinations of declared `[Flags]` bits. Malformed, undefined, or overflowing key
segments return 400 before hooks or repository access. Scalar enum keys use the same
membership rule after ASP.NET route binding.

JSON-backed resources use a `Key` object instead of `KeyProperty`:

```json
{
  "EntityType": "TenantProduct, MyApi",
  "Name": "tenant-products",
  "Route": "/api/tenant-products",
  "AllowAnonymousAll": true,
  "Key": {
    "Properties": ["TenantId", "Sku"],
    "RouteParameters": ["tenantId", "sku"]
  }
}
```
