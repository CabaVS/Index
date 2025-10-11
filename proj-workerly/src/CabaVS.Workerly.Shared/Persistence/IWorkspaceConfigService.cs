using CabaVS.Workerly.Shared.Entities;
using CabaVS.Workerly.Shared.Models;

namespace CabaVS.Workerly.Shared.Persistence;

public enum SaveConnectionResult { Success, Forbidden, Invalid, Error }
public enum SaveTeamsResult { Success, Forbidden, Invalid, Error }

public interface IWorkspaceConfigService
{
    Task<WorkspaceConnection?> GetAsync(Guid workspaceId, CancellationToken ct);
    Task<SaveConnectionResult> UpsertAsync(Guid requesterUserId, Guid workspaceId,
        string organization, string pat, CancellationToken ct);
    
    Task<TeamsDefinition> GetTeamsAsync(Guid workspaceId, CancellationToken ct);
    Task<SaveTeamsResult> SaveTeamsAsync(Guid requesterUserId, Guid workspaceId, TeamsDefinition teams, CancellationToken ct);
}
