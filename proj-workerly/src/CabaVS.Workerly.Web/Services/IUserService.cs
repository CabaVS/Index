using CabaVS.Workerly.Web.Entities;

namespace CabaVS.Workerly.Web.Services;

internal interface IUserService
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task EnsureExistsAsync(User user, CancellationToken ct);
}

