using CabaVS.Workerly.Shared.Models;
using CabaVS.Workerly.Web.Configuration;
using CabaVS.Workerly.Web.Entities;
using Microsoft.Azure.Cosmos;

namespace CabaVS.Workerly.Web.Services;

internal sealed class CosmosWorkspaceConfigService(
    CosmosContext ctx,
    ILogger<CosmosWorkspaceConfigService> logger) : IWorkspaceConfigService
{
    public async Task<WorkspaceConnection?> GetAsync(Guid workspaceId, CancellationToken ct)
    {
        try
        {
            ItemResponse<WorkspaceConnection>? resp = await ctx.WorkspaceConfigs.ReadItemAsync<WorkspaceConnection>(
                id: workspaceId.ToString(),
                partitionKey: new PartitionKey(workspaceId.ToString()),
                cancellationToken: ct);

            logger.LogInformation("Loaded connection for workspace {WorkspaceId}. RU: {RU}",
                workspaceId, resp.RequestCharge);
            return resp.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogInformation("No connection doc for workspace {WorkspaceId}.", workspaceId);
            return null;
        }
    }

    public async Task<SaveConnectionResult> UpsertAsync(
        Guid requesterUserId, Guid workspaceId, string organization, string pat, CancellationToken ct)
    {
        logger.LogInformation("Saving connection for workspace {WorkspaceId} by {UserId}.",
            workspaceId, requesterUserId);

        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(pat))
        {
            logger.LogWarning("Save connection invalid input. Org or PAT empty. Workspace {WorkspaceId}, User {UserId}.",
                workspaceId, requesterUserId);
            return SaveConnectionResult.Invalid;
        }
        
        FeedIterator<UserWorkspace>? check = ctx.Memberships.GetItemQueryIterator<UserWorkspace>(
            new QueryDefinition("SELECT TOP 1 * FROM m WHERE m.userId = @uid")
                .WithParameter("@uid", requesterUserId),
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(workspaceId.ToString())
            });

        UserWorkspace? membership = null;
        if (check.HasMoreResults)
        {
            FeedResponse<UserWorkspace>? page = await check.ReadNextAsync(ct);
            membership = page.Resource.FirstOrDefault();
        }

        if (membership is null || !membership.IsAdmin)
        {
            logger.LogWarning("Save connection forbidden. User {UserId} is not admin of workspace {WorkspaceId}.",
                requesterUserId, workspaceId);
            return SaveConnectionResult.Forbidden;
        }

        var doc = new WorkspaceConnection
        {
            WorkspaceId = workspaceId,
            Organization = organization.Trim(),
            PersonalAccessToken = pat.Trim()
        };

        try
        {
            ItemResponse<WorkspaceConnection>? resp = await ctx.WorkspaceConfigs.UpsertItemAsync(
                doc, new PartitionKey(workspaceId.ToString()), cancellationToken: ct);

            logger.LogInformation("Connection saved for workspace {WorkspaceId}. RU: {RU}",
                workspaceId, resp.RequestCharge);
            return SaveConnectionResult.Success;
        }
        catch (CosmosException ex)
        {
            logger.LogError(ex, "Cosmos failure saving connection for workspace {WorkspaceId} by {UserId}.",
                workspaceId, requesterUserId);
            return SaveConnectionResult.Error;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure saving connection for workspace {WorkspaceId} by {UserId}.",
                workspaceId, requesterUserId);
            return SaveConnectionResult.Error;
        }
    }

    public async Task<TeamsDefinition> GetTeamsAsync(Guid workspaceId, CancellationToken ct)
    {
        try
        {
            ItemResponse<WorkspaceConnection>? resp = await ctx.WorkspaceConfigs.ReadItemAsync<WorkspaceConnection>(
                workspaceId.ToString(), new PartitionKey(workspaceId.ToString()), cancellationToken: ct);
            logger.LogInformation("Loaded teams for workspace {WorkspaceId}. RU: {RU}", workspaceId,
                resp.RequestCharge);
            
            return resp.Resource.TeamsDefinition;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogInformation("No config doc found when loading teams for workspace {WorkspaceId}.", workspaceId);
            return new TeamsDefinition([]);
        }
    }

    public async Task<SaveTeamsResult> SaveTeamsAsync(
        Guid requesterUserId, Guid workspaceId, TeamsDefinition teams, CancellationToken ct)
    {
        logger.LogInformation("Saving teams for workspace {WorkspaceId} by user {UserId}.", workspaceId,
            requesterUserId);
        
        FeedIterator<UserWorkspace>? check = ctx.Memberships.GetItemQueryIterator<UserWorkspace>(
            new QueryDefinition("SELECT TOP 1 * FROM m WHERE m.userId = @uid")
                .WithParameter("@uid", requesterUserId),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(workspaceId.ToString()) });

        UserWorkspace? membership = null;
        if (check.HasMoreResults)
        {
            FeedResponse<UserWorkspace>? page = await check.ReadNextAsync(ct);
            membership = page.Resource.FirstOrDefault();
        }

        if (membership is null)
        {
            logger.LogWarning("SaveTeams forbidden: user {UserId} is not a member of workspace {WorkspaceId}.",
                requesterUserId, workspaceId);
            return SaveTeamsResult.Forbidden;
        }
        
        WorkspaceConnection doc;
        try
        {
            ItemResponse<WorkspaceConnection>? read = await ctx.WorkspaceConfigs.ReadItemAsync<WorkspaceConnection>(
                workspaceId.ToString(), new PartitionKey(workspaceId.ToString()), cancellationToken: ct);
            doc = read.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            doc = new WorkspaceConnection { WorkspaceId = workspaceId };
        }

        doc.TeamsDefinition = teams;

        try
        {
            ItemResponse<WorkspaceConnection>? resp = await ctx.WorkspaceConfigs.UpsertItemAsync(
                doc, new PartitionKey(workspaceId.ToString()), cancellationToken: ct);
            
            logger.LogInformation("Teams saved for workspace {WorkspaceId}. RU: {RU}", workspaceId, resp.RequestCharge);
            return SaveTeamsResult.Success;
        }
        catch (CosmosException ex)
        {
            logger.LogError(ex, "Cosmos failure saving teams for workspace {WorkspaceId} by {UserId}.",
                workspaceId, requesterUserId);
            return SaveTeamsResult.Error;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected failure saving teams for workspace {WorkspaceId} by {UserId}.",
                workspaceId, requesterUserId);
            return SaveTeamsResult.Error;
        }
    }
}
