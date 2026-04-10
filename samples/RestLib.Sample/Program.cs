using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using RestLib;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.Filtering;
using RestLib.InMemory;
using RestLib.Sample;
using RestLib.Sample.Models;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add RestLib services with in-memory repositories (pre-seeded)
// EnableETagSupport is a global option — applies to all resources (Categories, Products, and Orders)
// Validation is enabled by default (EnableValidation = true). The sample models use Data Annotation
// attributes ([Required], [Range], [StringLength], [EmailAddress]) so invalid payloads return a
// 400 Problem Details response with per-field errors. Try it:
//   curl -X POST http://localhost:5000/api/products -H 'Content-Type: application/json' -d '{}'
builder.Services.AddRestLib(opts =>
{
    opts.EnableETagSupport = true;
    opts.EnableHateoas = true;
});
builder.Services.AddRestLibInMemoryWithData(c => c.Id, Guid.NewGuid, SeedData.GetCategories());
builder.Services.AddRestLibInMemoryWithData(p => p.Id, Guid.NewGuid, SeedData.GetProducts());
builder.Services.AddRestLibInMemoryWithData(o => o.Id, Guid.NewGuid, SeedData.GetOrders());
builder.Services.AddNamedHook<Product, Guid>(HookNames.SetUpdatedAt, ctx =>
{
    if (ctx.Entity is Product product)
    {
        product.UpdatedAt = ctx.Operation == RestLibOperation.Create ? null : DateTime.UtcNow;
    }

    return Task.CompletedTask;
});

var categoryResource = builder.Configuration
    .GetSection("RestLib:Resources:Categories")
    .Get<RestLibJsonResourceConfiguration>()
    ?? throw new InvalidOperationException("Missing RestLib category resource configuration.");

var productResource = builder.Configuration
    .GetSection("RestLib:Resources:Products")
    .Get<RestLibJsonResourceConfiguration>()
    ?? throw new InvalidOperationException("Missing RestLib product resource configuration.");

builder.Services.AddJsonResource<Category, Guid>(categoryResource);
builder.Services.AddJsonResource<Product, Guid>(productResource);

// Register rate limiting policies
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("restlib-read", limiter =>
    {
        limiter.PermitLimit = 200;
        limiter.Window = TimeSpan.FromMinutes(1);
    });
    options.AddFixedWindowLimiter("restlib-write", limiter =>
    {
        limiter.PermitLimit = 200;
        limiter.Window = TimeSpan.FromMinutes(1);
    });
});

// Configure OpenAPI document with document transformer
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "RestLib Sample API",
            Version = "v1",
            Description = "A sample API demonstrating RestLib — full CRUD with pagination, filtering, and OpenAPI in just a few lines.",
            Contact = new OpenApiContact { Name = "RestLib", Url = new Uri("https://github.com/Adrian01987/RestLib") },
        };
        return Task.CompletedTask;
    });
});

// Health check endpoint for server readiness probing
builder.Services.AddHealthChecks();

var app = builder.Build();

// OpenAPI document + Scalar API Reference UI
app.MapOpenApi();
app.MapScalarApiReference("/", options =>
{
    options.WithTitle("RestLib Sample API")
           .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

// Health check endpoint
app.MapHealthChecks("/health");

// Map RestLib endpoints from JSON resource configuration (Categories + Products)
app.UseRateLimiter();
app.MapJsonResources();

// Map Order endpoints using the fluent C# API — demonstrates features not shown by JSON config above
app.MapRestLib<Order, Guid>("/api/orders", cfg =>
{
    // Explicit key selector (matches the default Id convention, shown here for demonstration)
    cfg.KeySelector = o => o.Id;

    // Operation selection — orders are replace-only, no PATCH
    cfg.ExcludeOperations(RestLibOperation.Patch);

    // Authorization — reads are public, writes require authentication (secure by default)
    // BatchCreate is included so the batch endpoint is accessible without auth middleware in this demo
    cfg.AllowAnonymous(RestLibOperation.GetAll, RestLibOperation.GetById, RestLibOperation.BatchCreate);

    // Filtering — equality for status, string operators for email, comparison for total
    cfg.AllowFiltering(o => o.Status, [FilterOperator.Eq, FilterOperator.Neq, FilterOperator.In]);
    cfg.AllowFiltering(o => o.CustomerEmail, FilterOperators.String);
    cfg.AllowFiltering(o => o.Total, FilterOperators.Comparison);

    // Sorting via strongly-typed expressions with a default sort
    cfg.AllowSorting(o => o.CreatedAt, o => o.Total);
    cfg.DefaultSort("created_at:desc");

    // Field selection — clients can request only the fields they need
    cfg.AllowFieldSelection(
        o => o.Id,
        o => o.CustomerEmail,
        o => o.Status,
        o => o.Total,
        o => o.CreatedAt);

    // Rate limiting — reuse the same policies, but exempt GetById
    cfg.UseRateLimiting("restlib-read", RestLibOperation.GetAll, RestLibOperation.GetById);
    cfg.UseRateLimiting("restlib-write", RestLibOperation.Create, RestLibOperation.Update, RestLibOperation.Delete);
    cfg.DisableRateLimiting(RestLibOperation.GetById);

    // Batch operations — allow bulk create and delete for orders
    cfg.EnableBatch(BatchAction.Create, BatchAction.Delete);

    // Inline hooks via UseHooks (as opposed to named hooks used by Products via JSON config)
    cfg.UseHooks(hooks =>
    {
        // Auto-calculate total from order lines and set timestamps before persisting
        hooks.BeforePersist = ctx =>
      {
          if (ctx.Entity is Order order)
          {
              order.Total = order.Lines.Sum(l => l.Quantity * l.UnitPrice);

              if (ctx.Operation == RestLibOperation.Create)
              {
                  order.CreatedAt = DateTime.UtcNow;
                  order.UpdatedAt = null;
              }
              else
              {
                  order.UpdatedAt = DateTime.UtcNow;
              }
          }

          return Task.CompletedTask;
      };

        // Custom error handling — log and return a structured error response
        hooks.OnError = ctx =>
      {
          Console.WriteLine($"[Order Error] {ctx.Operation}: {ctx.Exception.Message}");
          return Task.CompletedTask;
      };
    });

    // OpenAPI metadata via fluent API
    cfg.OpenApi.Tag = "Order";
    cfg.OpenApi.TagDescription = "Manage customer orders";
    cfg.OpenApi.Summaries.GetAll = "List orders";
    cfg.OpenApi.Summaries.GetById = "Get order by id";
    cfg.OpenApi.Summaries.Create = "Place a new order";
    cfg.OpenApi.Summaries.Update = "Replace an order";
    cfg.OpenApi.Summaries.Delete = "Cancel an order";
    cfg.OpenApi.Descriptions.GetAll = "Returns a paginated list of orders. Supports filtering by status and customer_email, and sorting by created_at or total.";
});

// --- Versioned API groups (URL prefix strategy) ---
// Demonstrates registering the same entity type with different configurations per version.
// GET /api/v1/products — read-only, no sorting or field selection
// GET /api/v2/products — full CRUD with sorting and field selection
var v1 = app.MapGroup("/api/v1");
var v2 = app.MapGroup("/api/v2");

v1.MapRestLib<Product, Guid>("/products", cfg =>
{
    cfg.AllowAnonymous();
    cfg.KeySelector = p => p.Id;
    cfg.ExcludeOperations(
        RestLibOperation.Create,
        RestLibOperation.Update,
        RestLibOperation.Patch,
        RestLibOperation.Delete,
        RestLibOperation.BatchCreate,
        RestLibOperation.BatchUpdate,
        RestLibOperation.BatchPatch,
        RestLibOperation.BatchDelete);
    cfg.AllowFiltering(p => p.CategoryId);
    cfg.OpenApi.Tag = "Products (v1)";
});

v2.MapRestLib<Product, Guid>("/products", cfg =>
{
    cfg.AllowAnonymous();
    cfg.KeySelector = p => p.Id;
    cfg.AllowFiltering(p => p.CategoryId, p => p.IsActive);
    cfg.AllowSorting(p => p.Price, p => p.Name, p => p.CreatedAt);
    cfg.DefaultSort("name:asc");
    cfg.AllowFieldSelection(
        p => p.Id,
        p => p.Name,
        p => p.Price,
        p => p.IsActive);
    cfg.OpenApi.Tag = "Products (v2)";
});

// Custom statistics endpoint for categories
app.MapGet("/api/categories/statistics", async (IRepository<Category, Guid> repository, CancellationToken ct) =>
{
    // Fetch all categories (limit set to max value to get all items)
    var page = await repository.GetAllAsync(new RestLib.Pagination.PaginationRequest { Limit = int.MaxValue }, ct);
    var categories = page.Items;

    var stats = new
    {
        TotalCategories = categories.Count,
        Names = categories.Select(c => c.Name)
    };

    return Results.Ok(stats);
})
.WithTags("Category")
.WithSummary("Get category statistics")
.WithDescription("Returns aggregated statistics about categories and their products.")
.AllowAnonymous();

app.Run();
