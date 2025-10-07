using CabaVS.Workerly.Web.Models;

namespace CabaVS.Workerly.Web.Services;

internal interface IWorkspaceService
{
    Task<IReadOnlyList<WorkspaceListItem>> GetForUserAsync(Guid userId, CancellationToken ct);
    Task SetSelectedAsync(Guid userId, Guid workspaceId, CancellationToken ct);
    Task<Guid> CreateAsync(string name, Guid ownerUserId, CancellationToken ct);
}
