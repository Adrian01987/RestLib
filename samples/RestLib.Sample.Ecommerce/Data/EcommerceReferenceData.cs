using RestLib.Sample.Ecommerce.Models;
using RestLib.Sample.Ecommerce.Payments;

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

    /// <summary>
    /// Gets the seeded country reference data.
    /// </summary>
    /// <returns>The seeded countries.</returns>
    public static IReadOnlyList<Country> GetCountries()
    {
        return
        [
            new Country
            {
                Code = "US",
                Name = "United States",
                Region = "North America",
                CurrencyCode = "USD",
                IsShippingEnabled = true,
            },
            new Country
            {
                Code = "CA",
                Name = "Canada",
                Region = "North America",
                CurrencyCode = "CAD",
                IsShippingEnabled = true,
            },
            new Country
            {
                Code = "GB",
                Name = "United Kingdom",
                Region = "Europe",
                CurrencyCode = "GBP",
                IsShippingEnabled = true,
            },
            new Country
            {
                Code = "DE",
                Name = "Germany",
                Region = "Europe",
                CurrencyCode = "EUR",
                IsShippingEnabled = true,
            },
            new Country
            {
                Code = "AU",
                Name = "Australia",
                Region = "Asia-Pacific",
                CurrencyCode = "AUD",
                IsShippingEnabled = false,
            },
        ];
    }

    /// <summary>
    /// Gets the seeded payment method reference data.
    /// </summary>
    /// <returns>The seeded payment methods.</returns>
    public static IReadOnlyList<PaymentMethod> GetPaymentMethods()
    {
        return
        [
            new PaymentMethod
            {
                Key = PaymentMethods.Card,
                DisplayName = "Card",
                Description = "Immediate card authorization through the fake payment processor.",
                IsEnabled = true,
                SortOrder = 10,
            },
            new PaymentMethod
            {
                Key = PaymentMethods.PayPal,
                DisplayName = "PayPal",
                Description = "Wallet authorization through the fake PayPal strategy.",
                IsEnabled = true,
                SortOrder = 20,
            },
            new PaymentMethod
            {
                Key = PaymentMethods.BankTransfer,
                DisplayName = "Bank transfer",
                Description = "Offline bank transfer simulation for delayed settlement examples.",
                IsEnabled = true,
                SortOrder = 30,
            },
        ];
    }
}
