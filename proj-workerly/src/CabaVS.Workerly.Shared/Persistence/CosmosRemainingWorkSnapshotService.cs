using CabaVS.Workerly.Shared.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace CabaVS.Workerly.Shared.Persistence;

internal sealed class CosmosRemainingWorkSnapshotService(
    ILogger<CosmosRemainingWorkSnapshotService> logger,
    CosmosContext ctx) : IRemainingWorkSnapshotService
{
    public async Task<string> CreateAsync(Guid workspaceId, RemainingWorkSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Root is null)
        {
            throw new InvalidOperationException("Snapshot is not eligible for persistence.");
        }
        
        await ctx.RemainingWorkSnapshots.CreateItemAsync(snapshot, new PartitionKey(workspaceId.ToString()), cancellationToken: ct);
        logger.LogInformation("Snapshot {SnapshotId} created successfully.", snapshot.Id);
        
        return snapshot.Id;
    }
}
