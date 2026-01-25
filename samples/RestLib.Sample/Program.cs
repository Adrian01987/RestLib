using Microsoft.OpenApi.Models;
using RestLib;
using RestLib.InMemory;
using RestLib.Sample.Models;

var builder = WebApplication.CreateBuilder(args);

// Add RestLib services with in-memory repositories (pre-seeded)
builder.Services.AddRestLib();
builder.Services.AddRestLibInMemoryWithData<Category, Guid>(c => c.Id, Guid.NewGuid, SeedData.GetCategories());
builder.Services.AddRestLibInMemoryWithData<Product, Guid>(p => p.Id, Guid.NewGuid, SeedData.GetProducts());

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

// Map RestLib endpoints — the magic ✨
app.MapRestLib<Category, Guid>("/api/categories", config => config.AllowAnonymous());
app.MapRestLib<Product, Guid>("/api/products", config =>
{
  config.AllowAnonymous();
  config.AllowFiltering(p => p.CategoryId, p => p.IsActive);
  config.UseHooks(hooks => hooks.BeforePersist = ctx =>
  {
    if (ctx.Entity is Product p) p.UpdatedAt = ctx.Operation == RestLibOperation.Create ? null : DateTime.UtcNow;
    return Task.CompletedTask;
  });
});

app.Run();
