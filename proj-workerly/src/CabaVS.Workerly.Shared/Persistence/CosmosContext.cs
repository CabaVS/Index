using Microsoft.Azure.Cosmos;

namespace CabaVS.Workerly.Shared.Persistence;

public sealed class CosmosContext
{
    public required CosmosClient Client { get; init; }
    public required Database Database { get; init; }
    public required Container Users { get; init; }
    public required Container Workspaces { get; init; }
    public required Container WorkspaceConfigs { get; init; }
    public required Container Memberships { get; init; }
    public required Container RemainingWorkSnapshots { get; init; }
}
