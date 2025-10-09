using CabaVS.Workerly.Web.Entities;

namespace CabaVS.Workerly.Web.Services;

internal enum SaveConnectionResult { Success, Forbidden, Invalid, Error }

internal interface IWorkspaceConfigService
{
    Task<WorkspaceConnection?> GetAsync(Guid workspaceId, CancellationToken ct);
    Task<SaveConnectionResult> UpsertAsync(Guid requesterUserId, Guid workspaceId,
        string organization, string pat, CancellationToken ct);
}
