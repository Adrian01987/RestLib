using Microsoft.EntityFrameworkCore;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Data;

/// <summary>
/// Seeds the ecommerce sample database with initial actor and workflow data.
/// </summary>
public static class EcommerceSeedData
{
    /// <summary>
    /// Ensures the database contains initial demo data.
    /// </summary>
    /// <param name="db">The ecommerce database context.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when seeding is finished.</returns>
    public static async Task EnsureSeededAsync(EcommerceDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (await db.Users.AnyAsync(ct))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var adminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var customerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var carrierId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var cartId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var productId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var orderId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var shipmentId = Guid.Parse("77777777-7777-7777-7777-777777777777");

        var admin = new User
        {
            Id = adminId,
            UserName = "admin",
            Email = "admin@example.com",
            PasswordHash = "phase1-placeholder",
            Role = "Admin",
            CreatedAt = now,
        };

        var customer = new User
        {
            Id = customerId,
            UserName = "customer",
            Email = "customer@example.com",
            PasswordHash = "phase1-placeholder",
            Role = "Customer",
            CreatedAt = now,
        };

        var carrier = new User
        {
            Id = carrierId,
            UserName = "carrier",
            Email = "carrier@example.com",
            PasswordHash = "phase1-placeholder",
            Role = "Carrier",
            CreatedAt = now,
        };

        var address = new Address
        {
            Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
            CustomerId = customerId,
            Line1 = "100 Market Street",
            City = "Seattle",
            Region = "WA",
            PostalCode = "98101",
            CountryCode = "US",
            IsPrimary = true,
            CreatedAt = now,
        };

        var phone = new Phone
        {
            Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            CustomerId = customerId,
            Number = "+1-206-555-0100",
            Type = "Mobile",
            IsPrimary = true,
            CreatedAt = now,
        };

        var cart = new Cart
        {
            Id = cartId,
            CustomerId = customerId,
            Status = "ACTIVE",
            CreatedAt = now,
        };

        cart.Items.Add(new CartItem
        {
            CartId = cartId,
            ProductId = productId,
            Quantity = 1,
            UnitPrice = 49.99m,
            LineTotal = 49.99m,
        });

        var order = new Order
        {
            Id = orderId,
            CustomerId = customerId,
            Status = "ASSIGNED",
            PaymentMethod = "card",
            Total = 49.99m,
            CreatedAt = now,
        };

        order.Items.Add(new OrderItem
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            OrderId = orderId,
            ProductId = productId,
            ProductName = "Seed Product",
            Quantity = 1,
            UnitPrice = 49.99m,
            LineTotal = 49.99m,
        });

        order.Payment = new Payment
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            OrderId = orderId,
            Method = "card",
            Status = "PAID",
            Amount = 49.99m,
            ExternalReference = "seed-payment",
            PaidAt = now,
        };

        order.Shipment = new Shipment
        {
            Id = shipmentId,
            OrderId = orderId,
            CarrierId = carrierId,
            Status = "ASSIGNED",
            CreatedAt = now,
        };

        order.Shipment.Events.Add(new ShipmentEvent
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            ShipmentId = shipmentId,
            Status = "ASSIGNED",
            Notes = "Seed shipment assigned to demo carrier.",
            OccurredAt = now,
        });

        var supportTicket = new SupportTicket
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            CreatedByUserId = customerId,
            Subject = "Seed support ticket",
            Message = "A seeded ticket for support workflow examples.",
            Status = "OPEN",
            CreatedAt = now,
        };

        db.Users.AddRange(admin, customer, carrier);
        db.AddRange(address, phone, cart, order, supportTicket);

        await db.SaveChangesAsync(ct);
    }
}
