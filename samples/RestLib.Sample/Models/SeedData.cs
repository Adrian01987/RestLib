namespace RestLib.Sample.Models;

/// <summary>
/// Pre-seeded sample data for the demo.
/// </summary>
public static class SeedData
{
  public static readonly Guid ElectronicsId = Guid.Parse("11111111-1111-1111-1111-111111111111");
  public static readonly Guid BooksId = Guid.Parse("22222222-2222-2222-2222-222222222222");
  public static readonly Guid ClothingId = Guid.Parse("33333333-3333-3333-3333-333333333333");

  public static IEnumerable<Category> GetCategories() =>
  [
      new() { Id = ElectronicsId, Name = "Electronics", Description = "Gadgets and devices", CreatedAt = DateTime.UtcNow },
      new() { Id = BooksId, Name = "Books", Description = "Physical and digital books", CreatedAt = DateTime.UtcNow },
      new() { Id = ClothingId, Name = "Clothing", Description = "Apparel and accessories", CreatedAt = DateTime.UtcNow }
  ];

  public static readonly Guid HeadphonesId = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
  public static readonly Guid KeyboardId = Guid.Parse("aaaa2222-2222-2222-2222-222222222222");
  public static readonly Guid CleanCodeId = Guid.Parse("aaaa3333-3333-3333-3333-333333333333");
  public static readonly Guid TShirtId = Guid.Parse("aaaa4444-4444-4444-4444-444444444444");

  public static IEnumerable<Product> GetProducts() =>
  [
      new() { Id = HeadphonesId, Name = "Wireless Headphones", Description = "Noise-canceling Bluetooth headphones", Price = 149.99m, CategoryId = ElectronicsId, CreatedAt = DateTime.UtcNow },
      new() { Id = KeyboardId, Name = "Mechanical Keyboard", Description = "RGB backlit mechanical keyboard", Price = 89.99m, CategoryId = ElectronicsId, CreatedAt = DateTime.UtcNow },
      new() { Id = Guid.NewGuid(), Name = "USB-C Hub", Description = "7-in-1 USB-C multiport adapter", Price = 49.99m, CategoryId = ElectronicsId, CreatedAt = DateTime.UtcNow },
      new() { Id = CleanCodeId, Name = "Clean Code", Description = "A Handbook of Agile Software Craftsmanship", Price = 39.99m, CategoryId = BooksId, CreatedAt = DateTime.UtcNow },
      new() { Id = Guid.NewGuid(), Name = "The Pragmatic Programmer", Description = "Your Journey to Mastery", Price = 44.99m, CategoryId = BooksId, CreatedAt = DateTime.UtcNow },
      new() { Id = Guid.NewGuid(), Name = "Design Patterns", Description = "Elements of Reusable Object-Oriented Software", Price = 54.99m, CategoryId = BooksId, CreatedAt = DateTime.UtcNow },
      new() { Id = TShirtId, Name = "Cotton T-Shirt", Description = "Premium cotton crew neck t-shirt", Price = 24.99m, CategoryId = ClothingId, CreatedAt = DateTime.UtcNow },
      new() { Id = Guid.NewGuid(), Name = "Denim Jeans", Description = "Classic fit blue jeans", Price = 59.99m, CategoryId = ClothingId, CreatedAt = DateTime.UtcNow }
  ];

  public static IEnumerable<Order> GetOrders() =>
  [
      new()
      {
        Id = Guid.NewGuid(),
        CustomerEmail = "alice@example.com",
        Status = "Completed",
        Total = 239.98m,
        CreatedAt = DateTime.UtcNow.AddDays(-5),
        Lines =
        [
            new() { ProductId = HeadphonesId, Quantity = 1, UnitPrice = 149.99m },
            new() { ProductId = KeyboardId, Quantity = 1, UnitPrice = 89.99m }
        ]
      },
      new()
      {
        Id = Guid.NewGuid(),
        CustomerEmail = "bob@example.com",
        Status = "Pending",
        Total = 64.98m,
        CreatedAt = DateTime.UtcNow.AddDays(-1),
        Lines =
        [
            new() { ProductId = CleanCodeId, Quantity = 1, UnitPrice = 39.99m },
            new() { ProductId = TShirtId, Quantity = 1, UnitPrice = 24.99m }
        ]
      },
      new()
      {
        Id = Guid.NewGuid(),
        CustomerEmail = "alice@example.com",
        Status = "Shipped",
        Total = 449.97m,
        CreatedAt = DateTime.UtcNow.AddDays(-3),
        Lines =
        [
            new() { ProductId = HeadphonesId, Quantity = 3, UnitPrice = 149.99m }
        ]
      }
  ];
}
