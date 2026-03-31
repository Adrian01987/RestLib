using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using RestLib;
using RestLib.Abstractions;
using RestLib.Batch;
using RestLib.Configuration;
using RestLib.InMemory;
using RestLib.Sample;
using RestLib.Sample.Models;

var builder = WebApplication.CreateBuilder(args);

// Add RestLib services with in-memory repositories (pre-seeded)
// EnableETagSupport is a global option — applies to all resources (Categories, Products, and Orders)
builder.Services.AddRestLib(opts => { opts.EnableETagSupport = true; });
builder.Services.AddRestLibInMemoryWithData<Category, Guid>(c => c.Id, Guid.NewGuid, SeedData.GetCategories());
builder.Services.AddRestLibInMemoryWithData<Product, Guid>(p => p.Id, Guid.NewGuid, SeedData.GetProducts());
builder.Services.AddRestLibInMemoryWithData<Order, Guid>(o => o.Id, Guid.NewGuid, SeedData.GetOrders());
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
    limiter.PermitLimit = 100;
    limiter.Window = TimeSpan.FromMinutes(1);
  });
  options.AddFixedWindowLimiter("restlib-write", limiter =>
  {
    limiter.PermitLimit = 20;
    limiter.Window = TimeSpan.FromMinutes(1);
  });
});

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
  c.SwaggerDoc("v1", new OpenApiInfo
  {
    Title = "RestLib Sample API",
    Version = "v1",
    Description = "A sample API demonstrating RestLib — full CRUD with pagination, filtering, and OpenAPI in just a few lines.",
    Contact = new OpenApiContact { Name = "RestLib", Url = new Uri("https://github.com/Adrian01987/RestLib") }
  });
});

var app = builder.Build();

// Swagger UI at root
app.UseSwagger();
app.UseSwaggerUI(c =>
{
  c.SwaggerEndpoint("/swagger/v1/swagger.json", "RestLib Sample API v1");
  c.RoutePrefix = string.Empty;
  c.DocumentTitle = "RestLib Sample API";
  c.DefaultModelsExpandDepth(-1);
});

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

  // Filtering via strongly-typed expressions
  cfg.AllowFiltering(o => o.Status, o => o.CustomerEmail);

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
