using CabaVS.Workerly.Web.Configuration;
using CabaVS.Workerly.Web.Entities;
using CabaVS.Workerly.Web.Models;
using Microsoft.Azure.Cosmos;
using User = CabaVS.Workerly.Web.Entities.User;

namespace CabaVS.Workerly.Web.Services;

internal sealed class CosmosWorkspaceService(ILogger<CosmosWorkspaceService> logger, CosmosContext ctx) : IWorkspaceService
{
    public async Task<IReadOnlyList<WorkspaceListItem>> GetForUserAsync(Guid userId, CancellationToken ct)
    {
        logger.LogInformation("Loading workspaces for user {UserId} from Cosmos DB.", userId);
        
        QueryDefinition? q = new QueryDefinition("SELECT * FROM m WHERE m.userId = @uid")
            .WithParameter("@uid", userId);

        var memberships = new List<UserWorkspace>();
        using FeedIterator<UserWorkspace>? iterator = ctx.Memberships.GetItemQueryIterator<UserWorkspace>(q);
        while (iterator.HasMoreResults)
        {
            FeedResponse<UserWorkspace>? page = await iterator.ReadNextAsync(ct);
            memberships.AddRange(page.Resource);
            
            logger.LogInformation("Fetched page with {Count} membership records (RU: {RequestCharge}).",
                page.Count, page.RequestCharge);
        }

        if (memberships.Count == 0)
        {
            logger.LogInformation("No workspace memberships found for user {UserId}.", userId);
            return [];
        }
        
        logger.LogInformation("Total memberships found for user {UserId}: {Count}.", userId, memberships.Count);
        
        WorkspaceListItem?[] results = await Task.WhenAll(
            memberships.Select(async m =>
            {
                try
                {
                    ItemResponse<Workspace>? resp = await ctx.Workspaces.ReadItemAsync<Workspace>(
                        m.WorkspaceId.ToString(),
                        new PartitionKey(m.WorkspaceId.ToString()),
                        cancellationToken: ct);
                    
                    logger.LogInformation("Read workspace {WorkspaceId} (RU: {RequestCharge}).",
                        m.WorkspaceId, resp.RequestCharge);

                    return new WorkspaceListItem(resp.Resource.Id, resp.Resource.Name, m.IsSelected, m.IsAdmin);
                }
                catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    logger.LogWarning("Workspace {WorkspaceId} not found for user {UserId}.", m.WorkspaceId, userId);
                    return null;
                }
            }));

        return results
            .Where(r => r is not null)
            .OrderByDescending(r => r!.IsSelected)
            .ThenBy(r => r!.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public async Task SetSelectedAsync(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        logger.LogInformation("Setting selected workspace {WorkspaceId} for user {UserId}.", workspaceId, userId);
        
        QueryDefinition? q = new QueryDefinition("SELECT * FROM m WHERE m.userId = @uid")
            .WithParameter("@uid", userId);

        var memberships = new List<UserWorkspace>();
        using FeedIterator<UserWorkspace>? iterator = ctx.Memberships.GetItemQueryIterator<UserWorkspace>(q);
        while (iterator.HasMoreResults)
        {
            FeedResponse<UserWorkspace>? page = await iterator.ReadNextAsync(ct);
            memberships.AddRange(page.Resource);
            
            logger.LogInformation("Fetched {Count} memberships in page (RU: {RequestCharge}).", page.Count, page.RequestCharge);
        }
        
        var updated = 0;
        foreach (UserWorkspace m in memberships)
        {
            var isSelected = m.WorkspaceId == workspaceId;
            if (isSelected == m.IsSelected)
            {
                continue;
            }
            
            var updatedMembership = new UserWorkspace
            {
                UserId = m.UserId,
                WorkspaceId = m.WorkspaceId,
                IsAdmin = m.IsAdmin,
                IsSelected = isSelected
            };

            await ctx.Memberships.UpsertItemAsync(
                updatedMembership,
                new PartitionKey(m.WorkspaceId.ToString()),
                cancellationToken: ct);
            
            updated++;
        }
        
        logger.LogInformation("Updated selection for {UpdatedCount} memberships (User: {UserId}, Workspace: {WorkspaceId}).",
            updated, userId, workspaceId);
    }

    public async Task<Guid> CreateAsync(string name, Guid ownerUserId, CancellationToken ct)
    {
        var workspace = new Workspace { Id = Guid.NewGuid(), Name = name };
        logger.LogInformation("Creating new workspace {WorkspaceId} ('{WorkspaceName}') for user {UserId}.",
            workspace.Id, workspace.Name, ownerUserId);
        
        await ctx.Workspaces.CreateItemAsync(workspace, new PartitionKey(workspace.Id.ToString()), cancellationToken: ct);
        logger.LogInformation("Workspace {WorkspaceId} created successfully.", workspace.Id);

        var membership = new UserWorkspace
        {
            UserId = ownerUserId,
            WorkspaceId = workspace.Id,
            IsAdmin = true,
            IsSelected = true
        };
        await ctx.Memberships.CreateItemAsync(membership, new PartitionKey(workspace.Id.ToString()), cancellationToken: ct);
        logger.LogInformation("Membership created for workspace {WorkspaceId} and user {UserId}.",
            workspace.Id, ownerUserId);

        await SetSelectedAsync(ownerUserId, workspace.Id, ct);
        logger.LogInformation("Workspace {WorkspaceId} set as selected for user {UserId}.",
            workspace.Id, ownerUserId);
        
        return workspace.Id;
    }

    public async Task<InviteUserResult> InviteUserByEmailAsync(Guid inviterUserId, Guid workspaceId, string email,
        CancellationToken ct)
    {
        logger.LogInformation("Inviting user '{Email}' to workspace {WorkspaceId} by inviter {InviterUserId}.",
            email, workspaceId, inviterUserId);
        
        FeedIterator<UserWorkspace>? adminCheck = ctx.Memberships.GetItemQueryIterator<UserWorkspace>(
            new QueryDefinition("SELECT TOP 1 * FROM m WHERE m.userId = @uid")
                .WithParameter("@uid", inviterUserId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(workspaceId.ToString()) });

        UserWorkspace? inviterMembership = null;
        if (adminCheck.HasMoreResults)
        {
            FeedResponse<UserWorkspace>? page = await adminCheck.ReadNextAsync(ct);
            inviterMembership = page.Resource.FirstOrDefault();
        }

        if (inviterMembership is null || !inviterMembership.IsAdmin)
        {
            logger.LogWarning("Invite denied: inviter {InviterUserId} is not a member or not admin of workspace {WorkspaceId}.",
                inviterUserId, workspaceId);
            return InviteUserResult.Forbidden;
        }
        
        var emailNorm = email.Trim().ToLowerInvariant();
        logger.LogInformation("Searching for target user with normalized email '{EmailNorm}'.", emailNorm);
        
        FeedIterator<User> userIterator = ctx.Users.GetItemQueryIterator<User>(
            new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.email = @e")
                .WithParameter("@e", emailNorm));
        
        User? targetUser = null;
        while (userIterator.HasMoreResults && targetUser is null)
        {
            FeedResponse<User>? page = await userIterator.ReadNextAsync(ct);
            targetUser = page.Resource.FirstOrDefault();
        }

        if (targetUser is null)
        {
            logger.LogWarning("Invite failed: user with email '{EmailNorm}' not found.", emailNorm);
            return InviteUserResult.UserNotFound;
        }
        
        logger.LogInformation("Checking if user {TargetUserId} already member of workspace {WorkspaceId}.",
            targetUser.Id, workspaceId);
        
        FeedIterator<UserWorkspace>? memCheck = ctx.Memberships.GetItemQueryIterator<UserWorkspace>(
            new QueryDefinition("SELECT TOP 1 * FROM m WHERE m.userId = @uid")
                .WithParameter("@uid", targetUser.Id),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(workspaceId.ToString()) });

        if (memCheck.HasMoreResults)
        {
            FeedResponse<UserWorkspace>? page = await memCheck.ReadNextAsync(ct);
            if (page.Resource.Any())
            {
                logger.LogInformation("Invite skipped: user {TargetUserId} already member of workspace {WorkspaceId}.",
                    targetUser.Id, workspaceId);
                return InviteUserResult.AlreadyMember;
            }
        }
        
        var membership = new UserWorkspace
        {
            UserId = targetUser.Id,
            WorkspaceId = workspaceId,
            IsAdmin = false,
            IsSelected = false
        };
        
        logger.LogInformation("Creating new membership for user {TargetUserId} in workspace {WorkspaceId}.",
            targetUser.Id, workspaceId);

        await ctx.Memberships.CreateItemAsync(
            membership,
            new PartitionKey(workspaceId.ToString()),
            cancellationToken: ct);
        
        logger.LogInformation("Membership created successfully for user {TargetUserId} in workspace {WorkspaceId}.",
            targetUser.Id, workspaceId);

        return InviteUserResult.Success;
    }
}
