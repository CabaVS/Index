using CabaVS.Workerly.Web.Models;

namespace CabaVS.Workerly.Web.Services;

internal interface IWorkspaceService
{
    Task<IReadOnlyList<WorkspaceListItem>> GetForUserAsync(Guid userId, CancellationToken ct);
    Task SetSelectedAsync(Guid userId, Guid workspaceId, CancellationToken ct);
    Task<Guid> CreateAsync(string name, Guid ownerUserId, CancellationToken ct);
    Task<InviteUserResult> InviteUserByEmailAsync(Guid inviterUserId, Guid workspaceId, string email, CancellationToken ct);
}

internal enum InviteUserResult { Success, UserNotFound, AlreadyMember, Forbidden, Error }
