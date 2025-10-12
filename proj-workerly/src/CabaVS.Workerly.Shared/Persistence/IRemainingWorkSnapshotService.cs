using CabaVS.Workerly.Shared.Models;

namespace CabaVS.Workerly.Shared.Persistence;

public interface IRemainingWorkSnapshotService
{
    Task<string> CreateAsync(Guid workspaceId, RemainingWorkSnapshot snapshot, CancellationToken ct);
}
