using Microsoft.OpenApi.Models;
using RestLib;
using RestLib.Abstractions;
using RestLib.Configuration;
using RestLib.InMemory;
using RestLib.Sample;
using RestLib.Sample.Models;

var builder = WebApplication.CreateBuilder(args);

// Add RestLib services with in-memory repositories (pre-seeded)
builder.Services.AddRestLib();
builder.Services.AddRestLibInMemoryWithData<Category, Guid>(c => c.Id, Guid.NewGuid, SeedData.GetCategories());
builder.Services.AddRestLibInMemoryWithData<Product, Guid>(p => p.Id, Guid.NewGuid, SeedData.GetProducts());
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

// Map RestLib endpoints from JSON resource configuration
app.MapJsonResources();


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
