using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RestLib;
using RestLib.InMemory;
using System.Net.Http.Json;
using System.Text.Json;

namespace RestLib.Benchmarks;

/// <summary>
/// Benchmarks comparing RestLib endpoints vs raw Minimal API endpoints.
/// Measures the overhead introduced by the RestLib library.
/// </summary>
/// <remarks>
/// Uses TestServer directly (not WebApplicationFactory) to avoid 
/// command-line argument conflicts with BenchmarkDotNet.
/// </remarks>
[MemoryDiagnoser]
[MarkdownExporter]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CrudBenchmarks
{
  private HttpClient _RestLibClient = null!;
  private HttpClient _rawClient = null!;
  private IHost _RestLibHost = null!;
  private IHost _rawHost = null!;
  private Guid _existingProductId;

  /// <summary>
  /// Number of products to seed for realistic GetAll pagination benchmarks.
  /// </summary>
  private const int SeedProductCount = 100;

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
  };

  [GlobalSetup]
  public void Setup()
  {
    // Setup RestLib test server
    _RestLibHost = CreateRestLibHost();
    _RestLibHost.Start();
    _RestLibClient = _RestLibHost.GetTestClient();

    // Setup Raw Minimal API test server
    _rawHost = CreateRawHost();
    _rawHost.Start();
    _rawClient = _rawHost.GetTestClient();

    // Seed multiple products for realistic GetAll pagination benchmarks
    _existingProductId = Guid.NewGuid();

    // First product is the one we'll use for GetById
    var primaryProduct = new BenchmarkProduct
    {
      Id = _existingProductId,
      Name = "Benchmark Product",
      Price = 99.99m
    };

    _RestLibClient.PostAsJsonAsync("/api/products", primaryProduct, JsonOptions).GetAwaiter().GetResult();
    _rawClient.PostAsJsonAsync("/api/products", primaryProduct).GetAwaiter().GetResult();

    // Seed additional products for GetAll benchmarks
    for (int i = 0; i < SeedProductCount - 1; i++)
    {
      var product = new BenchmarkProduct
      {
        Id = Guid.NewGuid(),
        Name = $"Product {i + 1}",
        Price = 10.00m + (i * 0.5m)
      };

      _RestLibClient.PostAsJsonAsync("/api/products", product, JsonOptions).GetAwaiter().GetResult();
      _rawClient.PostAsJsonAsync("/api/products", product).GetAwaiter().GetResult();
    }
  }

  [GlobalCleanup]
  public void Cleanup()
  {
    _RestLibClient?.Dispose();
    _rawClient?.Dispose();
    _RestLibHost?.Dispose();
    _rawHost?.Dispose();
  }

  // ========== GET by ID Benchmarks ==========

  [BenchmarkCategory("GetById")]
  [Benchmark(Baseline = true, Description = "Raw Minimal API - GET by ID")]
  public async Task<HttpResponseMessage> RawMinimalApi_GetById()
  {
    return await _rawClient.GetAsync($"/api/products/{_existingProductId}");
  }

  [BenchmarkCategory("GetById")]
  [Benchmark(Description = "RestLib - GET by ID")]
  public async Task<HttpResponseMessage> RestLib_GetById()
  {
    return await _RestLibClient.GetAsync($"/api/products/{_existingProductId}");
  }

  // ========== GET All Benchmarks ==========

  [BenchmarkCategory("GetAll")]
  [Benchmark(Baseline = true, Description = "Raw Minimal API - GET all")]
  public async Task<HttpResponseMessage> RawMinimalApi_GetAll()
  {
    return await _rawClient.GetAsync("/api/products");
  }

  [BenchmarkCategory("GetAll")]
  [Benchmark(Description = "RestLib - GET all")]
  public async Task<HttpResponseMessage> RestLib_GetAll()
  {
    return await _RestLibClient.GetAsync("/api/products");
  }

  // ========== POST Benchmarks ==========

  [BenchmarkCategory("Create")]
  [Benchmark(Baseline = true, Description = "Raw Minimal API - POST")]
  public async Task<HttpResponseMessage> RawMinimalApi_Create()
  {
    var product = new BenchmarkProduct
    {
      Id = Guid.NewGuid(),
      Name = "New Product",
      Price = 49.99m
    };
    return await _rawClient.PostAsJsonAsync("/api/products", product);
  }

  [BenchmarkCategory("Create")]
  [Benchmark(Description = "RestLib - POST")]
  public async Task<HttpResponseMessage> RestLib_Create()
  {
    var product = new BenchmarkProduct
    {
      Id = Guid.NewGuid(),
      Name = "New Product",
      Price = 49.99m
    };
    return await _RestLibClient.PostAsJsonAsync("/api/products", product, JsonOptions);
  }

  // ========== PUT Benchmarks ==========

  [BenchmarkCategory("Update")]
  [Benchmark(Baseline = true, Description = "Raw Minimal API - PUT")]
  public async Task<HttpResponseMessage> RawMinimalApi_Update()
  {
    var product = new BenchmarkProduct
    {
      Id = _existingProductId,
      Name = "Updated Product",
      Price = 149.99m
    };
    return await _rawClient.PutAsJsonAsync($"/api/products/{_existingProductId}", product);
  }

  [BenchmarkCategory("Update")]
  [Benchmark(Description = "RestLib - PUT")]
  public async Task<HttpResponseMessage> RestLib_Update()
  {
    var product = new BenchmarkProduct
    {
      Id = _existingProductId,
      Name = "Updated Product",
      Price = 149.99m
    };
    return await _RestLibClient.PutAsJsonAsync($"/api/products/{_existingProductId}", product, JsonOptions);
  }

  /// <summary>
  /// Creates a TestServer host with RestLib endpoints.
  /// </summary>
  private static IHost CreateRestLibHost()
  {
    return new HostBuilder()
      .ConfigureWebHost(webBuilder =>
      {
        webBuilder.UseTestServer();
        webBuilder.ConfigureServices(services =>
        {
          services.AddRestLib();
          services.AddRestLibInMemory<BenchmarkProduct, Guid>(p => p.Id, Guid.NewGuid);
          services.AddRouting();
          services.AddAuthorization();
        });
        webBuilder.Configure(app =>
        {
          app.UseRouting();
          app.UseAuthorization();
          app.UseEndpoints(endpoints =>
          {
            endpoints.MapRestLib<BenchmarkProduct, Guid>("/api/products", config =>
            {
              config.AllowAnonymous();
            });
          });
        });
      })
      .Build();
  }

  /// <summary>
  /// Creates a TestServer host with raw Minimal API endpoints.
  /// </summary>
  private static IHost CreateRawHost()
  {
    var store = new RawProductStore();

    return new HostBuilder()
      .ConfigureWebHost(webBuilder =>
      {
        webBuilder.UseTestServer();
        webBuilder.ConfigureServices(services =>
        {
          services.AddSingleton(store);
          services.AddRouting();
          services.AddAuthorization();
        });
        webBuilder.Configure(app =>
        {
          app.UseRouting();
          app.UseAuthorization();
          app.UseEndpoints(endpoints =>
          {
            endpoints.MapGet("/api/products", () => Results.Ok(store.GetAll()));

            endpoints.MapGet("/api/products/{id:guid}", (Guid id) =>
            {
              var product = store.GetById(id);
              return product is null ? Results.NotFound() : Results.Ok(product);
            });

            endpoints.MapPost("/api/products", (BenchmarkProduct product) =>
            {
              store.Add(product);
              return Results.Created($"/api/products/{product.Id}", product);
            });

            endpoints.MapPut("/api/products/{id:guid}", (Guid id, BenchmarkProduct product) =>
            {
              var existing = store.GetById(id);
              if (existing is null) return Results.NotFound();
              store.Update(id, product);
              return Results.Ok(product);
            });
          });
        });
      })
      .Build();
  }
}

/// <summary>
/// Product model used in benchmarks.
/// </summary>
public class BenchmarkProduct
{
  public Guid Id { get; set; }
  public string Name { get; set; } = string.Empty;
  public decimal Price { get; set; }
}

/// <summary>
/// Simple in-memory store for raw API benchmarks.
/// </summary>
public class RawProductStore
{
  private readonly Dictionary<Guid, BenchmarkProduct> _products = new();

  public IEnumerable<BenchmarkProduct> GetAll() => _products.Values;

  public BenchmarkProduct? GetById(Guid id) =>
      _products.TryGetValue(id, out var product) ? product : null;

  public void Add(BenchmarkProduct product) =>
      _products[product.Id] = product;

  public void Update(Guid id, BenchmarkProduct product)
  {
    product.Id = id;
    _products[id] = product;
  }
}
