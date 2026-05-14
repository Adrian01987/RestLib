using Microsoft.EntityFrameworkCore;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;

namespace RestLib.Sample.Ecommerce.Data;

/// <summary>
/// EF Core database context for the ecommerce sample.
/// </summary>
public class EcommerceDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// Initializes a new instance of the <see cref="EcommerceDbContext"/> class.
    /// </summary>
    /// <param name="options">The database context options.</param>
    /// <param name="currentUser">The current request actor.</param>
    public EcommerceDbContext(DbContextOptions<EcommerceDbContext> options, ICurrentUser currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    /// <summary>
    /// Gets the users set.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// Gets the categories set.
    /// </summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>
    /// Gets the products set.
    /// </summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>
    /// Gets the addresses set.
    /// </summary>
    public DbSet<Address> Addresses => Set<Address>();

    /// <summary>
    /// Gets the phone numbers set.
    /// </summary>
    public DbSet<Phone> Phones => Set<Phone>();

    /// <summary>
    /// Gets the carts set.
    /// </summary>
    public DbSet<Cart> Carts => Set<Cart>();

    /// <summary>
    /// Gets the cart items set.
    /// </summary>
    public DbSet<CartItem> CartItems => Set<CartItem>();

    /// <summary>
    /// Gets the orders set.
    /// </summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>
    /// Gets the order items set.
    /// </summary>
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    /// <summary>
    /// Gets the payments set.
    /// </summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>
    /// Gets the shipments set.
    /// </summary>
    public DbSet<Shipment> Shipments => Set<Shipment>();

    /// <summary>
    /// Gets the shipment events set.
    /// </summary>
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();

    /// <summary>
    /// Gets the support tickets set.
    /// </summary>
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();

    private Guid CurrentActorId => _currentUser.UserId ?? Guid.Empty;

    private bool IsAdmin => _currentUser.IsAdmin;

    private bool IsCustomer => _currentUser.IsCustomer;

    private bool IsCarrier => _currentUser.IsCarrier;

    /// <inheritdoc />
    public override int SaveChanges()
    {
        UpdateProductRowVersions();
        return base.SaveChanges();
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        UpdateProductRowVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateProductRowVersions();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        UpdateProductRowVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureUsers(modelBuilder);
        ConfigureCatalog(modelBuilder);
        ConfigureCustomerOwnedResources(modelBuilder);
        ConfigureCarts(modelBuilder);
        ConfigureOrders(modelBuilder);
        ConfigureFulfillment(modelBuilder);
        ConfigureSupport(modelBuilder);
    }

    private void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(user => user.UserName).IsUnique();
            entity.HasIndex(user => user.Email).IsUnique();

            entity.Property(user => user.UserName).HasMaxLength(100);
            entity.Property(user => user.Email).HasMaxLength(200);
            entity.Property(user => user.PasswordHash).HasMaxLength(500);
            entity.Property(user => user.Role).HasMaxLength(40);
        });
    }

    private void ConfigureCatalog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(category => category.Slug).IsUnique();

            entity.Property(category => category.Name).HasMaxLength(120);
            entity.Property(category => category.Slug).HasMaxLength(120);
            entity.Property(category => category.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasIndex(product => product.Sku).IsUnique();

            entity.HasOne(product => product.Category)
                .WithMany(category => category.Products)
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Navigation(product => product.Category).AutoInclude();
            entity.Property(product => product.Sku).HasMaxLength(80);
            entity.Property(product => product.Name).HasMaxLength(200);
            entity.Property(product => product.Description).HasMaxLength(1000);
            entity.Property(product => product.Price).HasPrecision(18, 2);
            entity.Property(product => product.RowVersion).IsConcurrencyToken();
        });
    }

    private void ConfigureCustomerOwnedResources(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Address>(entity =>
        {
            entity.HasQueryFilter(address =>
                IsAdmin || (IsCustomer && address.CustomerId == CurrentActorId));

            entity.HasOne(address => address.Customer)
                .WithMany(user => user.Addresses)
                .HasForeignKey(address => address.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(address => address.Line1).HasMaxLength(200);
            entity.Property(address => address.Line2).HasMaxLength(200);
            entity.Property(address => address.City).HasMaxLength(100);
            entity.Property(address => address.Region).HasMaxLength(100);
            entity.Property(address => address.PostalCode).HasMaxLength(30);
            entity.Property(address => address.CountryCode).HasMaxLength(2);
        });

        modelBuilder.Entity<Phone>(entity =>
        {
            entity.HasQueryFilter(phone =>
                IsAdmin || (IsCustomer && phone.CustomerId == CurrentActorId));

            entity.HasOne(phone => phone.Customer)
                .WithMany(user => user.Phones)
                .HasForeignKey(phone => phone.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(phone => phone.Number).HasMaxLength(40);
            entity.Property(phone => phone.Type).HasMaxLength(40);
        });
    }

    private void ConfigureCarts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cart>(entity =>
        {
            entity.HasQueryFilter(cart =>
                IsAdmin || (IsCustomer && cart.CustomerId == CurrentActorId));

            entity.HasOne(cart => cart.Customer)
                .WithMany(user => user.Carts)
                .HasForeignKey(cart => cart.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(cart => cart.Status).HasMaxLength(40);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.HasKey(item => new { item.CartId, item.ProductId });

            entity.HasOne(item => item.Cart)
                .WithMany(cart => cart.Items)
                .HasForeignKey(item => item.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(item => item.UnitPrice).HasPrecision(18, 2);
            entity.Property(item => item.LineTotal).HasPrecision(18, 2);
        });
    }

    private void ConfigureOrders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasQueryFilter(order =>
                IsAdmin || (IsCustomer && order.CustomerId == CurrentActorId));

            entity.HasOne(order => order.Customer)
                .WithMany(user => user.Orders)
                .HasForeignKey(order => order.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(order => order.Status).HasMaxLength(40);
            entity.Property(order => order.PaymentMethod).HasMaxLength(40);
            entity.Property(order => order.Total).HasPrecision(18, 2);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.HasOne(item => item.Order)
                .WithMany(order => order.Items)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(item => item.ProductName).HasMaxLength(200);
            entity.Property(item => item.UnitPrice).HasPrecision(18, 2);
            entity.Property(item => item.LineTotal).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasIndex(payment => payment.OrderId).IsUnique();

            entity.HasOne(payment => payment.Order)
                .WithOne(order => order.Payment)
                .HasForeignKey<Payment>(payment => payment.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(payment => payment.Method).HasMaxLength(40);
            entity.Property(payment => payment.Status).HasMaxLength(40);
            entity.Property(payment => payment.Amount).HasPrecision(18, 2);
            entity.Property(payment => payment.ExternalReference).HasMaxLength(120);
        });
    }

    private void ConfigureFulfillment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.HasQueryFilter(shipment =>
                IsAdmin || (IsCarrier && shipment.CarrierId == CurrentActorId));

            entity.HasIndex(shipment => shipment.OrderId).IsUnique();

            entity.HasOne(shipment => shipment.Order)
                .WithOne(order => order.Shipment)
                .HasForeignKey<Shipment>(shipment => shipment.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(shipment => shipment.Carrier)
                .WithMany(user => user.AssignedShipments)
                .HasForeignKey(shipment => shipment.CarrierId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(shipment => shipment.Status).HasMaxLength(40);
        });

        modelBuilder.Entity<ShipmentEvent>(entity =>
        {
            entity.HasOne(shipmentEvent => shipmentEvent.Shipment)
                .WithMany(shipment => shipment.Events)
                .HasForeignKey(shipmentEvent => shipmentEvent.ShipmentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(shipmentEvent => shipmentEvent.Status).HasMaxLength(40);
            entity.Property(shipmentEvent => shipmentEvent.Notes).HasMaxLength(500);
        });
    }

    private void ConfigureSupport(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SupportTicket>(entity =>
        {
            entity.HasOne(ticket => ticket.CreatedByUser)
                .WithMany()
                .HasForeignKey(ticket => ticket.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(ticket => ticket.Subject).HasMaxLength(200);
            entity.Property(ticket => ticket.Message).HasMaxLength(2000);
            entity.Property(ticket => ticket.Status).HasMaxLength(40);
        });
    }

    private void UpdateProductRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries<Product>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion = BitConverter.GetBytes(DateTime.UtcNow.Ticks);
            }
        }
    }
}
