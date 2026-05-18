namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents a payment attempt or result for an order.
/// </summary>
public class Payment
{
    /// <summary>
    /// Gets or sets the payment identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the order.
    /// </summary>
    public Order? Order { get; set; }

    /// <summary>
    /// Gets or sets the payment method.
    /// </summary>
    public required string Method { get; set; }

    /// <summary>
    /// Gets or sets the payment status.
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Gets or sets the payment amount.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Gets or sets the external payment reference.
    /// </summary>
    public string? ExternalReference { get; set; }

    /// <summary>
    /// Gets or sets the payment timestamp.
    /// </summary>
    public DateTime? PaidAt { get; set; }
}
