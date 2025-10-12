using CabaVS.Workerly.Shared.Configuration;
using CabaVS.Workerly.Shared.Persistence;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace CabaVS.Workerly.Web.Extensions;

internal static class StartupExtensions
{
    public static async Task EnsureCosmosArtifactsAsync(this IServiceProvider sp, CancellationToken ct = default)
    {
        CosmosContext ctx = sp.GetRequiredService<CosmosContext>();
        CosmosOptions cfg = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;

        var dbName = cfg.Database;
        var cUsers = cfg.Containers.Users;
        var cWs = cfg.Containers.Workspaces;
        var cWsCfg = cfg.Containers.WorkspaceConfigs;
        var cMembers = cfg.Containers.Memberships;
        var cSnapshots = cfg.Containers.RemainingWorkSnapshots;
        
        DatabaseResponse? dbResp = await ctx.Client.CreateDatabaseIfNotExistsAsync(dbName, cancellationToken: ct);
        Database? db = dbResp.Database;
        
        var usersProps = new ContainerProperties(id: cUsers, partitionKeyPath: "/id")
        {
            UniqueKeyPolicy = new UniqueKeyPolicy
            {
                UniqueKeys = { new UniqueKey { Paths = { "/email" } } }
            }
        };
        await db.CreateContainerIfNotExistsAsync(usersProps, throughput: null, cancellationToken: ct);
        
        var wsProps = new ContainerProperties(id: cWs, partitionKeyPath: "/id");
        await db.CreateContainerIfNotExistsAsync(wsProps, cancellationToken: ct);
        
        var cfgProps = new ContainerProperties(id: cWsCfg, partitionKeyPath: "/workspaceId");
        await db.CreateContainerIfNotExistsAsync(cfgProps, cancellationToken: ct);
        
        var memProps = new ContainerProperties(id: cMembers, partitionKeyPath: "/workspaceId");
        await db.CreateContainerIfNotExistsAsync(memProps, cancellationToken: ct);
        
        var snapProps = new ContainerProperties(id: cSnapshots, partitionKeyPath: "/workspaceId");
        await db.CreateContainerIfNotExistsAsync(snapProps, cancellationToken: ct);
    }
}
