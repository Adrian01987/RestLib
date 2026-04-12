namespace RestLib.EntityFrameworkCore.Tests.Fakes;

/// <summary>
/// Provides seed data for EF Core integration tests.
/// </summary>
public static class SeedData
{
    /// <summary>
    /// Creates a list of product entities for test seeding.
    /// </summary>
    /// <param name="count">The number of products to create.</param>
    /// <returns>A list of product entities with deterministic data.</returns>
    public static List<ProductEntity> CreateProducts(int count)
    {
        return Enumerable.Range(0, count)
            .Select(CreateProduct)
            .ToList();
    }

    /// <summary>
    /// Creates a single product entity with deterministic data.
    /// </summary>
    /// <param name="index">The index used to generate deterministic values.</param>
    /// <returns>A single product entity.</returns>
    public static ProductEntity CreateProduct(int index = 0)
    {
        return new ProductEntity
        {
            Id = Guid.NewGuid(),
            ProductName = $"Product {index}",
            UnitPrice = (index + 1) * 10.99m,
            StockQuantity = (index + 1) * 5,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
            LastModifiedAt = null,
            OptionalDescription = index % 2 == 0 ? $"Description for product {index}" : null,
            IsActive = index % 3 != 0,
            CategoryId = null,
            Status = index % 2 == 0 ? "Active" : "Inactive"
        };
    }

    /// <summary>
    /// Creates a list of category entities for test seeding.
    /// </summary>
    /// <param name="count">The number of categories to create.</param>
    /// <returns>A list of category entities with deterministic data.</returns>
    public static List<CategoryEntity> CreateCategories(int count)
    {
        return Enumerable.Range(0, count)
            .Select(CreateCategory)
            .ToList();
    }

    /// <summary>
    /// Creates a single category entity with deterministic data.
    /// </summary>
    /// <param name="index">The index used to generate deterministic values.</param>
    /// <returns>A single category entity.</returns>
    public static CategoryEntity CreateCategory(int index = 0)
    {
        return new CategoryEntity
        {
            Id = Guid.NewGuid(),
            Name = $"Category {index}",
            Description = index % 2 == 0 ? $"Description for category {index}" : null,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index)
        };
    }
}
