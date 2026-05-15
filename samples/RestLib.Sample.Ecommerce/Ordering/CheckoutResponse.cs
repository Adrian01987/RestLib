using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Ordering;

/// <summary>
/// Response body returned after successful storefront checkout.
/// </summary>
public sealed class CheckoutResponse
{
    /// <summary>
    /// Gets or sets the created order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the created shipment identifier.
    /// </summary>
    public Guid ShipmentId { get; set; }

    /// <summary>
    /// Gets or sets the order status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the payment method captured on the order.
    /// </summary>
    public string PaymentMethod { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the order total.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    /// Gets or sets the order line items.
    /// </summary>
    public List<CheckoutLineItemResponse> Items { get; set; } = [];

    internal static CheckoutResponse FromOrder(Order order, Guid shipmentId)
    {
        return new CheckoutResponse
        {
            OrderId = order.Id,
            ShipmentId = shipmentId,
            Status = order.Status,
            PaymentMethod = order.PaymentMethod,
            Total = order.Total,
            Items = order.Items
                .Select(item => new CheckoutLineItemResponse
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal,
                })
                .ToList(),
        };
    }
}

/// <summary>
/// Response body for a checked-out order line item.
/// </summary>
public sealed class CheckoutLineItemResponse
{
    /// <summary>
    /// Gets or sets the product identifier.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the product name captured on the order.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ordered quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the unit price captured on the order.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Gets or sets the line total.
    /// </summary>
    public decimal LineTotal { get; set; }
}
