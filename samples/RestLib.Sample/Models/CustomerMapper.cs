using RestLib.Abstractions;

namespace RestLib.Sample.Models;

/// <summary>
/// Maps the sample customer EF entity to the public customer profile DTO.
/// </summary>
public sealed class CustomerMapper : IRestLibMapper<CustomerDto, Customer>
{
    /// <inheritdoc />
    public CustomerDto ToApi(Customer dbModel)
    {
        return new CustomerDto
        {
            Id = dbModel.Id,
            Name = dbModel.Name,
            Email = dbModel.Email,
            City = dbModel.City,
            IsActive = dbModel.IsActive,
            CreatedAt = dbModel.CreatedAt
        };
    }

    /// <inheritdoc />
    public Customer ToDb(CustomerDto apiModel)
    {
        return new Customer
        {
            Id = apiModel.Id,
            Name = apiModel.Name,
            Email = apiModel.Email,
            City = apiModel.City,
            IsActive = apiModel.IsActive,
            CreatedAt = apiModel.CreatedAt == default ? DateTime.UtcNow : apiModel.CreatedAt
        };
    }
}
