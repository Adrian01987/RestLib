using RestLib.Abstractions;

namespace RestLib.Sample.Models;

/// <summary>
/// Maps the email-keyed customer directory representation to the EF-backed customer entity.
/// </summary>
public sealed class CustomerDirectoryMapper : IRestLibMapper<CustomerDirectoryEntry, Customer>
{
    /// <inheritdoc />
    public CustomerDirectoryEntry ToApi(Customer dbModel)
    {
        return new CustomerDirectoryEntry
        {
            Email = dbModel.Email,
            Name = dbModel.Name,
            City = dbModel.City,
            IsActive = dbModel.IsActive
        };
    }

    /// <inheritdoc />
    public Customer ToDb(CustomerDirectoryEntry apiModel)
    {
        return new Customer
        {
            Id = Guid.NewGuid(),
            Email = apiModel.Email,
            Name = apiModel.Name,
            City = apiModel.City,
            IsActive = apiModel.IsActive,
            CreatedAt = DateTime.UtcNow
        };
    }
}
