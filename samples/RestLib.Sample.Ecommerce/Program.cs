using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using RestLib;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.EntityFrameworkCore;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Sample.Ecommerce.Admin;
using RestLib.Sample.Ecommerce.Auth;
using RestLib.Sample.Ecommerce.Catalog;
using RestLib.Sample.Ecommerce.Data;
using RestLib.Sample.Ecommerce.Identity;
using RestLib.Sample.Ecommerce.Models;
using RestLib.Sample.Ecommerce.Ordering;
using RestLib.Sample.Ecommerce.Storefront;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var useMigrations = builder.Configuration.GetValue<bool>("RestLibSample:UseMigrations");
var jwtSettings = JwtSettings.Load(builder.Configuration);

builder.Services.AddRestLib(options =>
{
    options.EnableETagSupport = true;
    options.EnableHateoas = true;
    options.RequireAuthorizationByDefault = true;
});
builder.Services.AddSingleton<IETagGenerator, EcommerceRowVersionETagGenerator>();
builder.Services.AddHateoasLinkProvider<Order, Guid, OrderLinkProvider>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "RestLib Ecommerce Sample API",
            Version = "v1",
            Description = "A reference ecommerce API demonstrating RestLib across core, InMemory, and EF Core adapters.",
            Contact = new OpenApiContact
            {
                Name = "RestLib",
                Url = new Uri("https://github.com/Adrian01987/RestLib"),
            },
        };

        return Task.CompletedTask;
    });
});

builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<IDomainEventDispatcher, InProcessDomainEventDispatcher>();
builder.Services.AddSingleton<CarrierAssignmentCursor>();
builder.Services.AddScoped<ICarrierAssignmentService, CarrierAssignmentService>();
builder.Services.AddScoped<IDomainEventHandler<OrderPlaced>, OrderPlacedHandler>();
builder.Services.AddScoped<INotificationService, ConsoleNotificationService>();
builder.Services.AddDbContext<EcommerceDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Ecommerce")
        ?? "Data Source=restlib-ecommerce.db";
    options.UseSqlite(connectionString);
});
builder.Services.AddRestLibEfCore<EcommerceDbContext, Category, Guid>();
builder.Services.AddRestLibEfCore<EcommerceDbContext, Product, Guid>();
builder.Services.AddRestLibEfCore<EcommerceDbContext, User, Guid>();
builder.Services.AddRestLibEfCore<EcommerceDbContext, Address, Guid>();
builder.Services.AddRestLibEfCore<EcommerceDbContext, Phone, Guid>();
builder.Services.AddRestLibEfCore<EcommerceDbContext, Cart, Guid>();
builder.Services.AddRestLibEfCore<EcommerceDbContext, CartItem, RestLibCompositeKey<Guid, Guid>>();
builder.Services.AddRestLibEfCore<EcommerceDbContext, Order, Guid>();
builder.Services.AddRestLibEfCore<EcommerceDbContext, OrderItem, Guid>();
builder.Services.AddRestLibInMemoryWithData<Carrier, Guid>(
    carrier => carrier.Id,
    Guid.NewGuid,
    EcommerceReferenceData.GetCarriers());
builder.Services.AddRestLibMapper<UserDto, User, UserMapper>();
builder.Services.AddNamedHook<Address, Guid>(
    CustomerProfileHooks.EnsureSinglePrimaryHookName,
    CustomerProfileHooks.EnsureSinglePrimaryAddressAsync);
builder.Services.AddNamedHook<Phone, Guid>(
    CustomerProfileHooks.EnsureSinglePrimaryHookName,
    CustomerProfileHooks.EnsureSinglePrimaryPhoneAsync);
builder.Services.AddNamedHook<Cart, Guid>(
    StorefrontCartHooks.EnsureActiveCartHookName,
    StorefrontCartHooks.EnsureActiveCartAsync);
builder.Services.AddNamedHook<CartItem, RestLibCompositeKey<Guid, Guid>>(
    StorefrontCartHooks.PrepareCartItemHookName,
    StorefrontCartHooks.PrepareCartItemAsync);
builder.Services.AddRestLibFromFolder("Models");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("Customer", policy => policy.RequireRole("Customer"));
    options.AddPolicy("Carrier", policy => policy.RequireRole("Carrier"));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EcommerceDbContext>();

    if (useMigrations)
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    await EcommerceSeedData.EnsureSeededAsync(db);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference("/", options =>
{
    options.WithTitle("RestLib Ecommerce Sample API")
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

app.MapHealthChecks("/health");
app.MapEcommerceAuthEndpoints();
app.MapStorefrontAccountEndpoints();
app.MapStorefrontCartEndpoints();
app.MapCarrierProvisioningEndpoints();
app.MapJsonResources();

app.MapRestLib<Carrier, Guid>("/api/admin/carriers", config =>
{
    config.ExcludeOperations(RestLibOperation.Create);
    config.RequirePolicyForOperations("Admin", Enum.GetValues<RestLibOperation>());
    config.AllowFiltering(carrier => carrier.IsActive);
    config.AllowFiltering(carrier => carrier.DisplayName, FilterOperators.String);
    config.AllowSorting(carrier => carrier.DisplayName, carrier => carrier.CreatedAt);
    config.DefaultSort("display_name:asc");
    config.AllowFieldSelection(
        carrier => carrier.Id,
        carrier => carrier.UserId,
        carrier => carrier.DisplayName,
        carrier => carrier.ServiceArea,
        carrier => carrier.IsActive,
        carrier => carrier.CreatedAt);
    config.OpenApi.Tag = "Admin Carriers";
    config.OpenApi.TagDescription = "Manage carrier reference data through the InMemory adapter.";
    config.OpenApi.Summaries.GetAll = "List carriers";
    config.OpenApi.Summaries.GetById = "Get carrier by id";
    config.OpenApi.Summaries.Update = "Replace carrier";
    config.OpenApi.Summaries.Patch = "Patch carrier";
    config.OpenApi.Summaries.Delete = "Delete carrier";
});

app.MapRestLib<Product, Guid>("/api/v2/admin/products", config =>
{
    config.RequirePolicyForOperations("Admin", Enum.GetValues<RestLibOperation>());
    config.AllowFiltering(product => product.CategoryId, product => product.IsActive);
    config.AllowFiltering(product => product.Price, FilterOperators.Comparison);
    config.AllowFiltering(product => product.Name, FilterOperators.String);
    config.AllowSorting(product => product.Price, product => product.Name, product => product.CreatedAt);
    config.DefaultSort("name:asc");
    config.AllowFieldSelection(fields =>
    {
        fields.UseNestedObjectsInResponse();
        fields.AddProperty(product => product.Id);
        fields.AddProperty(product => product.Sku);
        fields.AddProperty(product => product.Name);
        fields.AddProperty(product => product.Description);
        fields.AddProperty(product => product.Price);
        fields.AddProperty(product => product.StockOnHand);
        fields.AddProperty(product => product.CategoryId);
        fields.AddProperty(product => product.Category!.Name);
        fields.AddProperty(product => product.Category!.Slug);
    });
    config.EnableBatch(BatchAction.Create, BatchAction.Update, BatchAction.Patch);
    config.OpenApi.Tag = "Admin Catalog";
    config.OpenApi.TagDescription = "Manage products through the admin catalog surface.";
    config.OpenApi.Summaries.GetAll = "List admin products";
    config.OpenApi.Summaries.GetById = "Get admin product by id";
    config.OpenApi.Summaries.Create = "Create admin product";
    config.OpenApi.Summaries.Update = "Replace admin product";
    config.OpenApi.Summaries.Patch = "Patch admin product";
    config.OpenApi.Summaries.Delete = "Delete admin product";
});

var adminOrders = app.MapRestLib<Order, Guid>("/api/admin/orders", config =>
{
    config.ExcludeOperations(RestLibOperation.Patch);
    config.RequirePolicyForOperations("Admin", Enum.GetValues<RestLibOperation>());
    config.AllowFiltering(order => order.CustomerId, order => order.Status, order => order.PaymentMethod);
    config.AllowFiltering(order => order.Total, FilterOperators.Comparison);
    config.AllowSorting(order => order.CreatedAt, order => order.UpdatedAt, order => order.Total, order => order.Status);
    config.DefaultSort("created_at:desc");
    config.AllowFieldSelection(
        order => order.Id,
        order => order.CustomerId,
        order => order.Status,
        order => order.PaymentMethod,
        order => order.Total,
        order => order.CreatedAt,
        order => order.UpdatedAt);
    config.UseHooks(hooks =>
    {
        hooks.OnRequestReceived = OrderHooks.ApplyAdminOrderDefaultsAsync;
        hooks.BeforePersist = OrderHooks.PrepareAdminOrderAsync;
    });
    config.OpenApi.Tag = "Admin Orders";
    config.OpenApi.TagDescription = "Admin order management surface with status transition validation.";
    config.OpenApi.Summaries.GetAll = "List admin orders";
    config.OpenApi.Summaries.GetById = "Get admin order by id";
    config.OpenApi.Summaries.Create = "Create admin order";
    config.OpenApi.Summaries.Update = "Replace admin order";
    config.OpenApi.Summaries.Delete = "Delete admin order";
});
adminOrders.MapAdminOrderStatusPatch();

app.MapRestLib<OrderItem, Guid>("/api/admin/order-items", config =>
{
    config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
    config.RequirePolicyForOperations("Admin", RestLibOperation.GetAll, RestLibOperation.GetById);
    config.AllowFiltering(item => item.OrderId, item => item.ProductId);
    config.AllowFiltering(item => item.ProductName, FilterOperators.String);
    config.AllowSorting(item => item.OrderId, item => item.ProductName, item => item.LineTotal);
    config.DefaultSort("order_id:asc,product_name:asc");
    config.AllowFieldSelection(
        item => item.Id,
        item => item.OrderId,
        item => item.ProductId,
        item => item.ProductName,
        item => item.Quantity,
        item => item.UnitPrice,
        item => item.LineTotal);
    config.OpenApi.Tag = "Admin Orders";
    config.OpenApi.Summaries.GetAll = "List admin order items";
    config.OpenApi.Summaries.GetById = "Get admin order item by id";
});

var storefrontOrderSurface = app.MapGroup("/api/storefront")
    .WithTags("Storefront Orders");
storefrontOrderSurface.MapStorefrontCheckout();

storefrontOrderSurface.MapGroup("/orders").MapRestLib<Order, Guid>(config =>
{
    config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById, RestLibOperation.Create);
    config.RequirePolicyForOperations("Customer", RestLibOperation.GetAll, RestLibOperation.GetById, RestLibOperation.Create);
    config.AllowFiltering(order => order.Status, order => order.PaymentMethod);
    config.AllowSorting(order => order.CreatedAt, order => order.Total, order => order.Status);
    config.DefaultSort("created_at:desc");
    config.AllowFieldSelection(
        order => order.Id,
        order => order.CustomerId,
        order => order.Status,
        order => order.PaymentMethod,
        order => order.Total,
        order => order.CreatedAt,
        order => order.UpdatedAt);
    config.UseHooks(hooks =>
    {
        hooks.OnRequestReceived = OrderHooks.PrepareStorefrontOrderAsync;
        hooks.BeforePersist = OrderHooks.PrepareStorefrontOrderAsync;
    });
    config.OpenApi.Tag = "Storefront Orders";
    config.OpenApi.TagDescription = "Customer order surface scoped by EF Core query filters.";
    config.OpenApi.Summaries.GetAll = "List my orders";
    config.OpenApi.Summaries.GetById = "Get my order by id";
    config.OpenApi.Summaries.Create = "Create my order";
});

storefrontOrderSurface.MapGroup("/order-items").MapRestLib<OrderItem, Guid>(config =>
{
    config.IncludeOperations(RestLibOperation.GetAll, RestLibOperation.GetById);
    config.RequirePolicyForOperations("Customer", RestLibOperation.GetAll, RestLibOperation.GetById);
    config.AllowFiltering(item => item.OrderId, item => item.ProductId);
    config.AllowSorting(item => item.OrderId, item => item.ProductName, item => item.LineTotal);
    config.DefaultSort("order_id:asc,product_name:asc");
    config.AllowFieldSelection(
        item => item.Id,
        item => item.OrderId,
        item => item.ProductId,
        item => item.ProductName,
        item => item.Quantity,
        item => item.UnitPrice,
        item => item.LineTotal);
    config.OpenApi.Tag = "Storefront Orders";
    config.OpenApi.Summaries.GetAll = "List my order items";
    config.OpenApi.Summaries.GetById = "Get my order item by id";
});

app.Run();
