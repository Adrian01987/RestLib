namespace RestLib.Sample.Models;

/// <summary>
/// Represents a customer order.
/// </summary>
public class Order
{
    public Guid Id { get; set; }
    public required string CustomerEmail { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
    public decimal Total { get; set; }
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
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
