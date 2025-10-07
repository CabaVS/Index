using CabaVS.Workerly.Web.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace CabaVS.Workerly.Web.Extensions;

internal static class StartupExtensions
{
    public static void TryConfigureCosmosDbForLocalDevelopment(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
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
            .Select(p => p.Split('='))
            .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);

        builder.Configuration["Cosmos:Endpoint"] = parts["AccountEndpoint"];
        builder.Configuration["Cosmos:Key"] = parts["AccountKey"];
    }
    
    public static IServiceCollection AddCosmos(this IServiceCollection services, IConfiguration cfg)
    {
        services.Configure<CosmosOptions>(cfg.GetSection("Cosmos"));

        services.AddSingleton(sp =>
        {
            CosmosOptions opts = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;

            var client = new CosmosClient(opts.Endpoint, opts.Key, new CosmosClientOptions
            {
                ApplicationName = "Workerly-Web",
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.Default
                },
                AllowBulkExecution = true
            });

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
}
