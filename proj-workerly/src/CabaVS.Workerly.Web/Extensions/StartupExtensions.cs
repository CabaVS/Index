using CabaVS.Workerly.Web.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace CabaVS.Workerly.Web.Extensions;

internal static class StartupExtensions
{
    public static void TryConfigureCosmosDbForLocalDevelopment(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            return;
        }
    
        var cs = builder.Configuration.GetConnectionString("cosmos-cvs-idx-local");
        if (string.IsNullOrWhiteSpace(cs))
        {
            return;
        }
    
        var parts = cs
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p =>
            {
                var idx = p.IndexOf('=', StringComparison.OrdinalIgnoreCase);
                return new { Key = p[..idx], Value = p[(idx + 1)..] };
            })
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

        builder.Configuration["Cosmos:Endpoint"] = parts["AccountEndpoint"];
        builder.Configuration["Cosmos:Key"] = parts["AccountKey"];
    }
    
    public static IServiceCollection AddCosmos(this IServiceCollection services, IConfiguration cfg, IWebHostEnvironment env)
    {
        services.Configure<CosmosOptions>(cfg.GetSection("Cosmos"));

        services.AddSingleton(sp =>
        {
            CosmosOptions opts = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;

            var cosmosClientOptions = new CosmosClientOptions
            {
                AllowBulkExecution = true,
                ApplicationName = $"Workerly-Web-{env.EnvironmentName}",
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            };
            
            // Some of those options are required because of the open issue
            // https://github.com/dotnet/aspire/issues/5364
            if (env.IsDevelopment())
            {
                cosmosClientOptions.ConnectionMode = ConnectionMode.Gateway;
                cosmosClientOptions.LimitToEndpoint = true;
                cosmosClientOptions.HttpClientFactory = () =>
                {
                    var handler = new HttpClientHandler
                    {
#pragma warning disable S4830
                        ServerCertificateCustomValidationCallback =
#pragma warning restore S4830
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    };
                    return new HttpClient(handler);
                };
            }
            
            var client = new CosmosClient(opts.Endpoint, opts.Key, cosmosClientOptions);

            Database? db = client.GetDatabase(opts.Database);

            return new CosmosContext
            {
                Client = client,
                Database = db,
                Users = db.GetContainer(opts.Containers.Users),
                Workspaces = db.GetContainer(opts.Containers.Workspaces),
                Memberships = db.GetContainer(opts.Containers.Memberships)
            };
        });

        return services;
    }
    
    public static async Task EnsureCosmosArtifactsAsync(this IServiceProvider sp, CancellationToken ct = default)
    {
        CosmosContext ctx = sp.GetRequiredService<CosmosContext>();
        CosmosOptions cfg = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;

        var dbName = cfg.Database;
        var cUsers = cfg.Containers.Users;
        var cWs = cfg.Containers.Workspaces;
        var cMembers = cfg.Containers.Memberships;
        
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
        
        var memProps = new ContainerProperties(id: cMembers, partitionKeyPath: "/workspaceId");
        await db.CreateContainerIfNotExistsAsync(memProps, cancellationToken: ct);
    }
}
