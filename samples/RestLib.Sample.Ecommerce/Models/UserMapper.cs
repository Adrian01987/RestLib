using RestLib.Abstractions;

namespace RestLib.Sample.Ecommerce.Models;

/// <summary>
/// Maps persisted ecommerce users to the safe administrative API model.
/// </summary>
public sealed class UserMapper : IRestLibMapper<UserDto, User>
{
    /// <inheritdoc />
    public UserDto ToApi(User dbModel)
    {
        return new UserDto
        {
            Id = dbModel.Id,
            UserName = dbModel.UserName,
            Email = dbModel.Email,
            Role = dbModel.Role,
            IsActive = dbModel.IsActive,
            CreatedAt = dbModel.CreatedAt
        };
    }

    /// <inheritdoc />
    public User ToDb(UserDto apiModel)
    {
        return new User
        {
            Id = apiModel.Id,
            UserName = apiModel.UserName,
            Email = apiModel.Email,
            PasswordHash = string.Empty,
            Role = apiModel.Role,
            IsActive = apiModel.IsActive,
            CreatedAt = apiModel.CreatedAt
        };
    }
}
