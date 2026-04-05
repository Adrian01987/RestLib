using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestLib.Abstractions;
using RestLib.InMemory;
using RestLib.Pagination;
using Xunit;

namespace RestLib.Tests;

public class InMemoryServiceExtensionsTests
{
  private record Product(Guid Id, string Name, decimal Price);

  #region Basic Registration Tests

  [Fact]
  public void AddRestLibInMemory_RegistersIRepository()
  {
    var services = new ServiceCollection();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    var provider = services.BuildServiceProvider();

    var repository = provider.GetService<IRepository<Product, Guid>>();

    repository.Should().NotBeNull();
    repository.Should().BeOfType<InMemoryRepository<Product, Guid>>();
  }

  [Fact]
  public void AddRestLibInMemory_RegistersConcreteRepository()
  {
    var services = new ServiceCollection();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    var provider = services.BuildServiceProvider();

    var repository = provider.GetService<InMemoryRepository<Product, Guid>>();

    repository.Should().NotBeNull();
  }

  [Fact]
  public void AddRestLibInMemory_BothRegistrationsReturnSameInstance()
  {
    var services = new ServiceCollection();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    var provider = services.BuildServiceProvider();

    var interface1 = provider.GetService<IRepository<Product, Guid>>();
    var concrete = provider.GetService<InMemoryRepository<Product, Guid>>();

    interface1.Should().BeSameAs(concrete);
  }

  [Fact]
  public void AddRestLibInMemory_RegistersIBatchRepository()
  {
    // Arrange
    var services = new ServiceCollection();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    var provider = services.BuildServiceProvider();

    // Act
    var batchRepository = provider.GetService<IBatchRepository<Product, Guid>>();

    // Assert
    batchRepository.Should().NotBeNull();
    batchRepository.Should().BeOfType<InMemoryRepository<Product, Guid>>();
  }

  [Fact]
  public void AddRestLibInMemory_AllRegistrationsReturnSameInstance()
  {
    // Arrange
    var services = new ServiceCollection();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    var provider = services.BuildServiceProvider();

    // Act
    var iRepository = provider.GetService<IRepository<Product, Guid>>();
    var iBatchRepository = provider.GetService<IBatchRepository<Product, Guid>>();
    var concrete = provider.GetService<InMemoryRepository<Product, Guid>>();

    // Assert
    iRepository.Should().BeSameAs(concrete);
    iBatchRepository.Should().BeSameAs(concrete);
  }

  [Fact]
  public void AddRestLibInMemory_ReturnsSameServicesForChaining()
  {
    var services = new ServiceCollection();
    var result = services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    result.Should().BeSameAs(services);
  }

  [Fact]
  public void AddRestLibInMemory_IsSingleton()
  {
    var services = new ServiceCollection();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    var provider = services.BuildServiceProvider();

    var repo1 = provider.GetService<IRepository<Product, Guid>>();
    var repo2 = provider.GetService<IRepository<Product, Guid>>();

    repo1.Should().BeSameAs(repo2);
  }

  #endregion

  #region With Seed Data Tests

  [Fact]
  public void AddRestLibInMemoryWithData_PopulatesRepository()
  {
    var services = new ServiceCollection();
    var seedData = new[]
    {
            new Product(Guid.NewGuid(), "Product 1", 10.00m),
            new Product(Guid.NewGuid(), "Product 2", 20.00m),
            new Product(Guid.NewGuid(), "Product 3", 30.00m)
        };

    services.AddRestLibInMemoryWithData<Product, Guid>(p => p.Id, Guid.NewGuid, seedData);
    var provider = services.BuildServiceProvider();
    var repository = provider.GetRequiredService<InMemoryRepository<Product, Guid>>();

    repository.Count.Should().Be(3);
  }

  [Fact]
  public async Task AddRestLibInMemoryWithData_AllDataAccessible()
  {
    var services = new ServiceCollection();
    var id1 = Guid.NewGuid();
    var id2 = Guid.NewGuid();
    var seedData = new[]
    {
            new Product(id1, "Product 1", 10.00m),
            new Product(id2, "Product 2", 20.00m)
        };

    services.AddRestLibInMemoryWithData<Product, Guid>(p => p.Id, Guid.NewGuid, seedData);
    var provider = services.BuildServiceProvider();
    var repository = provider.GetRequiredService<IRepository<Product, Guid>>();

    var product1 = await repository.GetByIdAsync(id1);
    var product2 = await repository.GetByIdAsync(id2);

    product1.Should().NotBeNull();
    product1!.Name.Should().Be("Product 1");
    product2.Should().NotBeNull();
    product2!.Name.Should().Be("Product 2");
  }

  [Fact]
  public void AddRestLibInMemoryWithData_EmptySeedData_CreatesEmptyRepository()
  {
    var services = new ServiceCollection();
    var seedData = Array.Empty<Product>();

    services.AddRestLibInMemoryWithData<Product, Guid>(p => p.Id, Guid.NewGuid, seedData);
    var provider = services.BuildServiceProvider();
    var repository = provider.GetRequiredService<InMemoryRepository<Product, Guid>>();

    repository.Count.Should().Be(0);
  }

  [Fact]
  public void AddRestLibInMemoryWithData_RegistersIBatchRepository()
  {
    // Arrange
    var services = new ServiceCollection();
    var seedData = new[] { new Product(Guid.NewGuid(), "Product 1", 10m) };
    services.AddRestLibInMemoryWithData<Product, Guid>(p => p.Id, Guid.NewGuid, seedData);
    var provider = services.BuildServiceProvider();

    // Act
    var batchRepository = provider.GetService<IBatchRepository<Product, Guid>>();

    // Assert
    batchRepository.Should().NotBeNull();
    batchRepository.Should().BeSameAs(provider.GetService<IRepository<Product, Guid>>());
  }

  [Fact]
  public void AddRestLibInMemoryWithOptions_RegistersIBatchRepository()
  {
    // Arrange
    var services = new ServiceCollection();
    services.AddRestLibInMemoryWithOptions<Product, Guid>(
        p => p.Id, Guid.NewGuid, new System.Text.Json.JsonSerializerOptions());
    var provider = services.BuildServiceProvider();

    // Act
    var batchRepository = provider.GetService<IBatchRepository<Product, Guid>>();

    // Assert
    batchRepository.Should().NotBeNull();
    batchRepository.Should().BeSameAs(provider.GetService<IRepository<Product, Guid>>());
  }

  [Fact]
  public void AddRestLibInMemoryWithDataAndOptions_RegistersIBatchRepository()
  {
    // Arrange
    var services = new ServiceCollection();
    var seedData = new[] { new Product(Guid.NewGuid(), "Product 1", 10m) };
    services.AddRestLibInMemoryWithDataAndOptions<Product, Guid>(
        p => p.Id, Guid.NewGuid, seedData, new System.Text.Json.JsonSerializerOptions());
    var provider = services.BuildServiceProvider();

    // Act
    var batchRepository = provider.GetService<IBatchRepository<Product, Guid>>();

    // Assert
    batchRepository.Should().NotBeNull();
    batchRepository.Should().BeSameAs(provider.GetService<IRepository<Product, Guid>>());
  }

  #endregion

  #region Multiple Entity Types Tests

  private record Order(int Id, string OrderNumber, decimal Total);
  private record Customer(string Code, string Name, string Email);

  [Fact]
  public void AddRestLibInMemory_MultipleEntityTypes_AllRegistered()
  {
    var services = new ServiceCollection();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    services.AddRestLibInMemory<Order, int>(o => o.Id, () => Random.Shared.Next());
    services.AddRestLibInMemory<Customer, string>(c => c.Code, () => Guid.NewGuid().ToString("N")[..8]);
    var provider = services.BuildServiceProvider();

    provider.GetService<IRepository<Product, Guid>>().Should().NotBeNull();
    provider.GetService<IRepository<Order, int>>().Should().NotBeNull();
    provider.GetService<IRepository<Customer, string>>().Should().NotBeNull();
  }

  [Fact]
  public async Task AddRestLibInMemory_MultipleEntityTypes_IndependentStores()
  {
    var services = new ServiceCollection();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    services.AddRestLibInMemory<Order, int>(o => o.Id, () => 1);
    var provider = services.BuildServiceProvider();

    var productRepo = provider.GetRequiredService<IRepository<Product, Guid>>();
    var orderRepo = provider.GetRequiredService<IRepository<Order, int>>();

    await productRepo.CreateAsync(new Product(Guid.NewGuid(), "Test Product", 10m));
    await orderRepo.CreateAsync(new Order(1, "ORD-001", 100m));

    var products = await productRepo.GetAllAsync(new PaginationRequest { Limit = 10 });
    var orders = await orderRepo.GetAllAsync(new PaginationRequest { Limit = 10 });

    products.Items.Should().HaveCount(1);
    orders.Items.Should().HaveCount(1);
  }

  #endregion

  #region Integration with RestLib Tests

  [Fact]
  public async Task AddRestLibInMemory_CanBeUsedWithRestLibEndpoints()
  {
    var services = new ServiceCollection();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    var provider = services.BuildServiceProvider();

    var repository = provider.GetRequiredService<IRepository<Product, Guid>>();

    var product = new Product(Guid.NewGuid(), "New Product", 99.99m);
    var created = await repository.CreateAsync(product);
    var retrieved = await repository.GetByIdAsync(product.Id);
    var all = await repository.GetAllAsync(new PaginationRequest { Limit = 10 });
    var deleted = await repository.DeleteAsync(product.Id);
    var afterDelete = await repository.GetByIdAsync(product.Id);

    created.Should().Be(product);
    retrieved.Should().Be(product);
    all.Items.Should().ContainSingle().Which.Should().Be(product);
    deleted.Should().BeTrue();
    afterDelete.Should().BeNull();
  }

  #endregion

  #region Key Generation Tests

  [Fact]
  public async Task AddRestLibInMemory_KeyGeneratorIsUsedForNewEntities()
  {
    var generatedId = Guid.NewGuid();
    var services = new ServiceCollection();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, () => generatedId);
    var provider = services.BuildServiceProvider();
    var repository = provider.GetRequiredService<IRepository<Product, Guid>>();

    var product = new Product(Guid.Empty, "New Product", 50m);
    var created = await repository.CreateAsync(product);

    created.Id.Should().Be(generatedId);
  }

  [Fact]
  public async Task AddRestLibInMemory_KeySelectorIsUsed()
  {
    var services = new ServiceCollection();
    var productId = Guid.NewGuid();
    services.AddRestLibInMemory<Product, Guid>(p => p.Id, Guid.NewGuid);
    var provider = services.BuildServiceProvider();
    var repository = provider.GetRequiredService<IRepository<Product, Guid>>();

    var product = new Product(productId, "Test", 10m);
    await repository.CreateAsync(product);
    var retrieved = await repository.GetByIdAsync(productId);

    retrieved.Should().NotBeNull();
    retrieved.Should().Be(product);
  }

  #endregion

  #region Custom Key Types Tests

  private record EntityWithIntKey(int Id, string Data);
  private record EntityWithStringKey(string Code, string Name);
  private record EntityWithCompositeKey(string Part1, int Part2, string Data)
  {
    public string CompositeKey => $"{Part1}-{Part2}";
  }

  [Fact]
  public async Task AddRestLibInMemory_WithIntKey_WorksCorrectly()
  {
    var services = new ServiceCollection();
    var counter = 0;
    services.AddRestLibInMemory<EntityWithIntKey, int>(e => e.Id, () => ++counter);
    var provider = services.BuildServiceProvider();
    var repository = provider.GetRequiredService<IRepository<EntityWithIntKey, int>>();

    var entity = new EntityWithIntKey(0, "Test");
    var created = await repository.CreateAsync(entity);
    var retrieved = await repository.GetByIdAsync(1);

    created.Id.Should().Be(1);
    retrieved.Should().NotBeNull();
  }

  [Fact]
  public async Task AddRestLibInMemory_WithStringKey_WorksCorrectly()
  {
    var services = new ServiceCollection();
    services.AddRestLibInMemory<EntityWithStringKey, string>(e => e.Code, () => $"CODE-{Guid.NewGuid():N}"[..12]);
    var provider = services.BuildServiceProvider();
    var repository = provider.GetRequiredService<IRepository<EntityWithStringKey, string>>();

    var entity = new EntityWithStringKey("CUST-001", "Test Customer");
    await repository.CreateAsync(entity);
    var retrieved = await repository.GetByIdAsync("CUST-001");

    retrieved.Should().NotBeNull();
    retrieved!.Name.Should().Be("Test Customer");
  }

  [Fact]
  public async Task AddRestLibInMemory_WithCompositeKey_WorksCorrectly()
  {
    var services = new ServiceCollection();
    services.AddRestLibInMemory<EntityWithCompositeKey, string>(e => e.CompositeKey, () => $"AUTO-{Random.Shared.Next()}");
    var provider = services.BuildServiceProvider();
    var repository = provider.GetRequiredService<IRepository<EntityWithCompositeKey, string>>();

    var entity = new EntityWithCompositeKey("TYPE", 123, "Data");
    await repository.CreateAsync(entity);
    var retrieved = await repository.GetByIdAsync("TYPE-123");

    retrieved.Should().NotBeNull();
    retrieved!.Data.Should().Be("Data");
  }

  #endregion
}
