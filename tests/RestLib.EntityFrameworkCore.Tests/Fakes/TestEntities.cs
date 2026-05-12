using System.ComponentModel.DataAnnotations;

namespace RestLib.EntityFrameworkCore.Tests.Fakes;

/// <summary>
/// Status values used in EF Core sorting tests.
/// </summary>
public enum ProductLifecycle
{
    /// <summary>
    /// Draft value.
    /// </summary>
    Draft,

    /// <summary>
    /// Active value.
    /// </summary>
    Active,

    /// <summary>
    /// Archived value.
    /// </summary>
    Archived,
}

/// <summary>
/// Product entity used in EF Core integration tests.
/// Property shapes match <c>ProductEntity</c> in <c>RestLib.Tests</c>.
/// </summary>
public class ProductEntity
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the product name.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unit price.
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Gets or sets the stock quantity.
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>
    /// Gets or sets the date the product was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date the product was last modified.
    /// </summary>
    public DateTime? LastModifiedAt { get; set; }

    /// <summary>
    /// Gets or sets an optional description.
    /// </summary>
    public string? OptionalDescription { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the product is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the optional category identifier.
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Gets or sets the product status.
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Gets or sets the lifecycle state used in typed sorting tests.
    /// </summary>
    public ProductLifecycle Lifecycle { get; set; }
}

/// <summary>
/// Category entity used in EF Core integration tests.
/// Provides a second entity type for multi-entity test scenarios.
/// </summary>
public class CategoryEntity
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the category name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the date the category was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Composite-key entity used in EF Core integration tests.
/// </summary>
public class TenantProductEntity
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the SKU.
    /// </summary>
    [Required]
    [StringLength(64)]
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the product name.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unit price.
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Gets or sets the stock quantity.
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the product is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Customer entity used for nested EF Core path tests.
/// </summary>
public class OrderCustomerEntity
{
    /// <summary>
    /// Gets or sets the customer identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the customer name.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the customer email.
    /// </summary>
    [Required]
    [StringLength(200)]
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Order entity used for nested EF Core path tests.
/// </summary>
public class OrderEntity
{
    /// <summary>
    /// Gets or sets the order identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the order number.
    /// </summary>
    [Required]
    [StringLength(64)]
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total amount.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Gets or sets the customer identifier.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Gets or sets the customer navigation.
    /// </summary>
    public OrderCustomerEntity? Customer { get; set; }
}
