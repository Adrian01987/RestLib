using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Data;

/// <summary>
/// Provides seeded reference data for in-memory ecommerce sample resources.
/// </summary>
public static class EcommerceReferenceData
{
    /// <summary>
    /// Gets the seeded carrier reference data.
    /// </summary>
    /// <returns>The seeded carriers.</returns>
    public static IReadOnlyList<Carrier> GetCarriers()
    {
        var carrierUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        return
        [
            new Carrier
            {
                Id = carrierUserId,
                UserId = carrierUserId,
                DisplayName = "Demo Carrier",
                ServiceArea = "Seattle metro",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            },
        ];
    }
}
