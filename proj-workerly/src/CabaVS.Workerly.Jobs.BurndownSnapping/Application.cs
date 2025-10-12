using System.Diagnostics;
using CabaVS.Workerly.Jobs.BurndownSnapping.Configuration;
using CabaVS.Workerly.Shared.Entities;
using CabaVS.Workerly.Shared.Models;
using CabaVS.Workerly.Shared.Persistence;
using CabaVS.Workerly.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace CabaVS.Workerly.Jobs.BurndownSnapping;

internal sealed class Application(
    ILogger<Application> logger,
    IOptions<RemainingWorkTrackerOptions> options,
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime) : IHostedService
{
    private static readonly ActivitySource ActivitySource = new(Constants.ActivityNames.RemainingWorkTracker);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using Activity? activity = ActivitySource.StartActivity();
        
        logger.LogInformation("Remaining Work Tracker started at {Timestamp} UTC.", DateTime.UtcNow);
        
        using IServiceScope scope = scopeFactory.CreateScope();
        IWorkspaceConfigService configService = scope.ServiceProvider.GetRequiredService<IWorkspaceConfigService>();
        IRemainingWorkSnapshotService remainingWorkSnapshotService = scope.ServiceProvider.GetRequiredService<IRemainingWorkSnapshotService>();
        AzureDevOpsIntegrationService azureDevOpsIntegrationService = scope.ServiceProvider.GetRequiredService<AzureDevOpsIntegrationService>();
        
        RemainingWorkTrackerOptions.ToTrackItem[] itemsToTrack = options.Value.ToTrackItems;
        if (itemsToTrack is { Length: 0 })
        {
            logger.LogInformation("No items to track.");
            return;
        }
        
        WorkspaceConnection? config = await configService.GetAsync(options.Value.WorkspaceId, cancellationToken);
        if (config is null || string.IsNullOrWhiteSpace(config.Organization) || string.IsNullOrWhiteSpace(config.PersonalAccessToken))
        {
            logger.LogWarning("AZDO connection missing for workspace {WorkspaceId}", options.Value.WorkspaceId);
            return;
        }
        
        foreach (RemainingWorkTrackerOptions.ToTrackItem item in itemsToTrack)
        {
            logger.LogInformation("Processing item {WorkItemId} from {From} to {To}.", item.WorkItemId, item.From, item.To);
            
            using var connection = new VssConnection(
                new Uri($"https://dev.azure.com/{config.Organization}"), 
                new VssBasicCredential(string.Empty, config.PersonalAccessToken));
            using WorkItemTrackingHttpClient client = await connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);

            RemainingWorkResponse? response = await azureDevOpsIntegrationService.ComputeRemainingWorkSnapshotAsync(
                client, item.WorkItemId, config.TeamsDefinition, cancellationToken);
            if (response is null)
            {
                logger.LogError("Failed to get remaining work for item {WorkItemId} from {From} to {To}.", item.WorkItemId, item.From, item.To);
                continue;
            }
            
            var snapshot = new RemainingWorkSnapshot(
                new Root(options.Value.WorkspaceId, response.Id, response.Title, DateTime.UtcNow),
                response.Report);
            logger.LogInformation("Snapshot computation successful for item {WorkItemId}. Proceeding to persisting.", item.WorkItemId);
            
            var persistedId = await remainingWorkSnapshotService.CreateAsync(options.Value.WorkspaceId, snapshot, cancellationToken);
            logger.LogInformation("Snapshot persisted with Id {SnapshotId}.", persistedId);
        }
        
        logger.LogInformation("Remaining Work Tracker finished at {Timestamp} UTC.", DateTime.UtcNow);
        
        lifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
