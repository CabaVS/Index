using CabaVS.Workerly.Shared.Entities;

namespace CabaVS.Workerly.Shared.Persistence;

public interface IUserService
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task EnsureExistsAsync(User user, CancellationToken ct);
}

