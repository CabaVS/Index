using Azure.Identity;
using CabaVS.Workerly.Shared.Configuration;
using CabaVS.Workerly.Shared.Persistence;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CabaVS.Workerly.Shared;

public static class DependencyInjection
{
    public static IServiceCollection AddCosmosPersistenceServices(
        this IServiceCollection services,
        IConfiguration cfg,
        bool isDevelopment,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        
        services.Configure<CosmosOptions>(cfg.GetSection("Cosmos"));
        
        services.AddSingleton(sp =>
        {
            CosmosOptions opts = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;

            var cosmosClientOptions = new CosmosClientOptions
            {
                AllowBulkExecution = true,
                ApplicationName = $"Workerly-Web-{environmentName}",
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            };

            CosmosClient client;
            
            if (isDevelopment)
            {
                // Some of those options are required because of the open issue
                // https://github.com/dotnet/aspire/issues/5364
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
                
                client = new CosmosClient(opts.Endpoint, opts.Key, cosmosClientOptions);
            }
            else
            {
                client = new CosmosClient(opts.Endpoint, new DefaultAzureCredential(), cosmosClientOptions);
            }

            Database? db = client.GetDatabase(opts.Database);

            return new CosmosContext
            {
                Client = client,
                Database = db,
                Users = db.GetContainer(opts.Containers.Users),
                Workspaces = db.GetContainer(opts.Containers.Workspaces),
                WorkspaceConfigs = db.GetContainer(opts.Containers.WorkspaceConfigs),
                Memberships = db.GetContainer(opts.Containers.Memberships)
            };
        });
        
        services
            .AddScoped<IWorkspaceService, CosmosWorkspaceService>()
            .AddScoped<IWorkspaceConfigService, CosmosWorkspaceConfigService>()
            .AddScoped<IUserService, CosmosUserService>();

        return services;
    }
    
    public static IServiceCollection TryConfigureCosmosDbForLocalDevelopment(
        this IServiceCollection services,
        IConfiguration cfg,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        
        if (!isDevelopment)
        {
            return services;
        }
    
        var cs = cfg.GetConnectionString("cosmos-cvs-idx-local");
        if (string.IsNullOrWhiteSpace(cs))
        {
            return services;
        }
    
        var parts = cs
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p =>
            {
                var idx = p.IndexOf('=', StringComparison.OrdinalIgnoreCase);
                return new { Key = p[..idx], Value = p[(idx + 1)..] };
            })
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

        cfg["Cosmos:Endpoint"] = parts["AccountEndpoint"];
        cfg["Cosmos:Key"] = parts["AccountKey"];

        return services;
    }
}
