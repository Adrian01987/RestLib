namespace RestLib.Sample.Models;

/// <summary>
/// Represents a product category.
/// </summary>
public class Category
{
  public Guid Id { get; set; }
  public required string Name { get; set; }
  public string? Description { get; set; }
  public DateTime CreatedAt { get; set; }
}
