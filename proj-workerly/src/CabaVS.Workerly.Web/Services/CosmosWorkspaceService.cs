using CabaVS.Workerly.Web.Configuration;
using CabaVS.Workerly.Web.Entities;
using CabaVS.Workerly.Web.Models;
using Microsoft.Azure.Cosmos;

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

                    return new WorkspaceListItem(resp.Resource.Id, resp.Resource.Name, m.IsSelected);
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
}
