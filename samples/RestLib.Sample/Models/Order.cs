using System.ComponentModel.DataAnnotations;

namespace RestLib.Sample.Models;

/// <summary>
/// Represents a customer order.
/// </summary>
public class Order
{
    public Guid Id { get; set; }

    [Required]
    [EmailAddress]
    public required string CustomerEmail { get; set; }

    public List<OrderLine> Lines { get; set; } = [];

    [Range(0, (double)decimal.MaxValue)]
    public decimal Total { get; set; }

    [Required]
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Represents a single line item within an order.
/// </summary>
public class OrderLine
{
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Range(0.01, (double)decimal.MaxValue)]
    public decimal UnitPrice { get; set; }
}
