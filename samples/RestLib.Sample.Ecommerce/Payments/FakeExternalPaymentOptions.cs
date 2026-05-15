namespace RestLib.Sample.Ecommerce.Payments;

/// <summary>
/// Options for the fake external payment client.
/// </summary>
public sealed class FakeExternalPaymentOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "RestLibSample:Payments:FakeExternalClient";

    /// <summary>
    /// Gets or sets the artificial processing latency in milliseconds.
    /// </summary>
    public int LatencyMilliseconds { get; set; } = 150;

    /// <summary>
    /// Gets or sets the simulated failure probability from 0.0 to 1.0.
    /// </summary>
    public double FailureRate { get; set; }

    /// <summary>
    /// Gets or sets the error code returned when the fake client simulates a failure.
    /// </summary>
    public string FailureErrorCode { get; set; } = "payment_declined";
}
