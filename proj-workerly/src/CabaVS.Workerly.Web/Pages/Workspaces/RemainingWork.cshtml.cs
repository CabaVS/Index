using System.Text.Json;
using CabaVS.Workerly.Shared.Entities;
using CabaVS.Workerly.Shared.Models;
using CabaVS.Workerly.Shared.Persistence;
using CabaVS.Workerly.Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace CabaVS.Workerly.Web.Pages.Workspaces;

internal sealed class RemainingWork(
    ILogger<RemainingWork> logger,
    AzureDevOpsIntegrationService azureDevOpsIntegrationService,
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
            RemainingWorkSnapshot remainingWorkSnapshot = await azureDevOpsIntegrationService
                .ComputeRemainingWorkSnapshotAsync(client, InputWorkItemId!.Value, config.TeamsDefinition, ct);

            WorkItemId = remainingWorkSnapshot.Root?.Id ?? InputWorkItemId;
            WorkItemTitle = remainingWorkSnapshot.Root?.Title ?? string.Empty;
            ExecutionDateUtc = remainingWorkSnapshot.Root?.ExecutionDateUtc ?? DateTime.UtcNow;
            SnapshotJson = JsonSerializer.Serialize(remainingWorkSnapshot, JsonSerializerOptions);

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
}
