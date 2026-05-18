namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Represents a support request opened by a customer or carrier.
/// </summary>
public class SupportTicket
{
    /// <summary>
    /// Gets or sets the support ticket identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the user identifier that opened the ticket.
    /// </summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// Gets or sets the user that opened the ticket.
    /// </summary>
    public User? CreatedByUser { get; set; }

    /// <summary>
    /// Gets or sets the ticket subject.
    /// </summary>
    public required string Subject { get; set; }

    /// <summary>
    /// Gets or sets the ticket message.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Gets or sets the ticket status.
    /// </summary>
    public string Status { get; set; } = "OPEN";

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
