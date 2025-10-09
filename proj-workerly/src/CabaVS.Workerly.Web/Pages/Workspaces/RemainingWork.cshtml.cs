using System.Globalization;
using System.Text.Json;
using CabaVS.Workerly.Shared.Models;
using CabaVS.Workerly.Web.Constants;
using CabaVS.Workerly.Web.Entities;
using CabaVS.Workerly.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace CabaVS.Workerly.Web.Pages.Workspaces;

internal sealed class RemainingWork(
    ILogger<RemainingWork> logger,
    IWorkspaceConfigService configService) : PageModel
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    
    [BindProperty(SupportsGet = true)]
    public Guid WorkspaceId { get; set; }

    [BindProperty]
    public int? InputWorkItemId { get; set; }

    // Output
    public int? WorkItemId { get; private set; }
    public string WorkItemTitle { get; private set; } = string.Empty;
    public DateTime ExecutionDateUtc { get; private set; }
    public string? SnapshotJson { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void OnGet() => logger.LogInformation("RemainingWork page GET. WorkspaceId={WorkspaceId}", WorkspaceId);
    
    public async Task<IActionResult> OnPostRunAsync(CancellationToken ct)
    {
        if (InputWorkItemId is null or <= 0)
        {
            ErrorMessage = "Please enter a valid work item id.";
            
            logger.LogWarning("RemainingWork run failed: invalid work item id. WorkspaceId={WorkspaceId}", WorkspaceId);
            return Page();
        }

        logger.LogInformation("RemainingWork run: workspace {WorkspaceId}, root WI {WorkItemId}",
            WorkspaceId, InputWorkItemId);

        try
        {
            WorkspaceConnection? config = await configService.GetAsync(WorkspaceId, ct);

            if (config is null || string.IsNullOrWhiteSpace(config.Organization) || string.IsNullOrWhiteSpace(config.PersonalAccessToken))
            {
                ErrorMessage = "Azure DevOps connection is not configured for this workspace.";
                
                logger.LogWarning("AZDO connection missing for workspace {WorkspaceId}", WorkspaceId);
                return Page();
            }
            
            using var connection = new VssConnection(
                new Uri($"https://dev.azure.com/{config.Organization}"), 
                new VssBasicCredential(string.Empty, config.PersonalAccessToken));
            using WorkItemTrackingHttpClient client = await connection.GetClientAsync<WorkItemTrackingHttpClient>(ct);

            // Compute
            Snapshot snapshot = await ComputeAsync(InputWorkItemId!.Value, client, config.TeamsDefinition, ct);

            WorkItemId = snapshot.Root?.Id ?? InputWorkItemId;
            WorkItemTitle = snapshot.Root?.Title ?? string.Empty;
            ExecutionDateUtc = snapshot.Root?.ExecutionDateUtc ?? DateTime.UtcNow;
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonSerializerOptions);

            logger.LogInformation("RemainingWork computed for WI {WorkItemId} in workspace {WorkspaceId}",
                WorkItemId, WorkspaceId);

            return Page();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RemainingWork error. WorkspaceId={WorkspaceId}, WI={WorkItemId}",
                WorkspaceId, InputWorkItemId);
            ErrorMessage = "Failed to compute remaining work. See logs for details.";
            return Page();
        }
    }
    
    private static async Task<Snapshot> ComputeAsync(
        int workItemId,
        WorkItemTrackingHttpClient workItemClient,
        TeamsDefinition teamsDefinitionOptions,
        CancellationToken cancellationToken)
    {
        WorkItem? root = await workItemClient.GetWorkItemAsync(
            workItemId,
            fields: [FieldNames.Title],
            cancellationToken: cancellationToken);

        if (root is null)
        {
            return new Snapshot(null, []);
        }

        var toTraverse = new HashSet<int> { root.Id!.Value };
        var collected = new List<WorkItem>();

        do
        {
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

        return new Snapshot(
            new Root(root.Id!.Value, 
                root.Fields.GetCastedValueOrDefault(FieldNames.Title, string.Empty),
                DateTime.UtcNow),
            groupedByTeam);
    }
    
    private sealed record Snapshot(Root? Root, RemainingWorkResponseItem[] Report);
    private sealed record Root(int Id, string Title, DateTime ExecutionDateUtc);
}
