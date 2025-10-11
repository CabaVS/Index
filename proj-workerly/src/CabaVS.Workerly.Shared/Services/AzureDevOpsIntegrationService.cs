using System.Globalization;
using CabaVS.Workerly.Shared.Constants;
using CabaVS.Workerly.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace CabaVS.Workerly.Shared.Services;

public sealed class AzureDevOpsIntegrationService(ILogger<AzureDevOpsIntegrationService> logger)
{
    public async Task<RemainingWorkSnapshot> ComputeRemainingWorkSnapshotAsync(
        WorkItemTrackingHttpClient workItemClient,
        int workItemId,
        TeamsDefinition teamsDefinitionOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItemClient);

        WorkItem? root = await workItemClient.GetWorkItemAsync(
            workItemId,
            fields: [FieldNames.Title],
            cancellationToken: cancellationToken);

        if (root is null)
        {
            logger.LogWarning("Root work item not found. WorkItemId: {WorkItemId}", workItemId);
            return new RemainingWorkSnapshot(null, []);
        }
        
        logger.LogInformation("Starting traversal for remaining work. Root WorkItemId: {WorkItemId}", workItemId);

        var toTraverse = new HashSet<int> { root.Id!.Value };
        var collected = new List<WorkItem>();

        do
        {
            logger.LogInformation("Processing batch of work items. Items to traverse: {Count}", toTraverse.Count);
            
            var currentBatch = (await Task.WhenAll(toTraverse
                    .Chunk(200)
                    .Select(batch => workItemClient.GetWorkItemsAsync(
                        ids: batch,
                        expand: WorkItemExpand.Relations,
                        errorPolicy: WorkItemErrorPolicy.Omit,
                        cancellationToken: cancellationToken))))
                .SelectMany(x => x)
                .DistinctBy(x => x.Id)
                .ToList();

            IEnumerable<WorkItem> tasksOrBugs = currentBatch
                .Where(wi =>
                    wi.Fields.GetCastedValueOrDefault(FieldNames.WorkItemType, string.Empty) is "Task" or "Bug")
                .ToArray();
            IEnumerable<WorkItem> otherTypes = currentBatch
                .ExceptBy(tasksOrBugs.Select(wi => wi.Id), wi => wi.Id)
                .ToArray();
            
            logger.LogInformation("Collected {TaskCount} tasks/bugs and {OtherCount} other work items.",
                tasksOrBugs.Count(), otherTypes.Count());

            collected.AddRange(
                tasksOrBugs
                    .Where(wi =>
                        wi.Fields.GetCastedValueOrDefault(FieldNames.State, string.Empty) is not "Closed"
                            and not "Removed"));

            toTraverse = otherTypes
                .Where(wi => wi.Relations is { Count: > 0 })
                .SelectMany(wi => wi.Relations)
                .Where(r => r.Rel == RelationshipNames.ParentToChild)
                .Select(r => int.Parse(r.Url.Split('/').LastOrDefault() ?? string.Empty, CultureInfo.InvariantCulture))
                .ToHashSet();

        } while (toTraverse.Count > 0);
        
        logger.LogInformation("Completed traversal for remaining work. WorkItemId: {WorkItemId}", workItemId);

        var groupedByAssignee = collected
            .Select(wi =>
            {
                var tags = wi.Fields.GetCastedValueOrDefault(FieldNames.Tags, string.Empty)?
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

                return new
                {
                    Assignee = wi.Fields.GetValueOrDefault(FieldNames.AssignedTo) is IdentityRef identityRef
                        ? identityRef.UniqueName.Split('@').FirstOrDefault(string.Empty)
                        : string.Empty,
                    RemainingWork = wi.Fields.GetCastedValueOrDefault(FieldNames.RemainingWork, 0.0),
                    RemainingWorkType = tags.DetermineFromTags()
                };
            })
            .GroupBy(x => x.Assignee)
            .Select(g =>
            {
                var lookupByType = g.ToLookup(x => x.RemainingWorkType);
                return new
                {
                    Assignee = string.IsNullOrWhiteSpace(g.Key) ? "UNKNOWN ASSIGNEE" : g.Key.ToUpperInvariant(),
                    TotalRemainingWork = new RemainingWorkModel(
                        lookupByType[RemainingWorkType.Functionality].Sum(x => x.RemainingWork),
                        lookupByType[RemainingWorkType.Requirements].Sum(x => x.RemainingWork),
                        lookupByType[RemainingWorkType.ReleaseFinalization].Sum(x => x.RemainingWork),
                        lookupByType[RemainingWorkType.Technical].Sum(x => x.RemainingWork),
                        lookupByType[RemainingWorkType.Other].Sum(x => x.RemainingWork))
                };
            })
            .OrderByDescending(x => x.TotalRemainingWork)
            .ThenBy(x => x.Assignee);

        RemainingWorkResponseItem[] groupedByTeam = groupedByAssignee
            .Select(x => new RemainingWorkResponseItem(
                teamsDefinitionOptions.Teams.SingleOrDefault(
                    y => y.Value.Contains(x.Assignee),
                    new KeyValuePair<string, HashSet<string>>($"UNKNOWN TEAM on {x.Assignee}", [])).Key,
                x.TotalRemainingWork))
            .GroupBy(x => x.Team)
            .Select(g =>
            {
                var team = g.Key.ToUpperInvariant()
                    .Replace("UNKNOWN TEAM ON UNKNOWN ASSIGNEE", "UNASSIGNED", StringComparison.Ordinal); 
                return new RemainingWorkResponseItem(
                    team,
                    g.Select(x => x.RemainingWork).Sum());
            })
            .OrderByDescending(x => x.RemainingWork)
            .ThenBy(x => x.Team)
            .ToArray();
        
        logger.LogInformation("Remaining work processing completed for WorkItemId: {WorkItemId}", workItemId);

        return new RemainingWorkSnapshot(
            new Root(root.Id!.Value, 
                root.Fields.GetCastedValueOrDefault(FieldNames.Title, string.Empty),
                DateTime.UtcNow),
            groupedByTeam);
    }
}
