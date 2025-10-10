using Aspire.Hosting.Azure;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ParameterResource> sqlPassword = builder.AddParameter("SqlPassword", secret: true);
IResourceBuilder<ParameterResource> keycloakOtelCollectorConfigFullPath = builder.AddParameter("KeycloakOtelCollectorConfigFullPath", secret: true);
IResourceBuilder<ParameterResource> keycloakLogOutputFullPath = builder.AddParameter("KeycloakLogOutputFullPath", secret: true);
IResourceBuilder<ParameterResource> keycloakUsername = builder.AddParameter("KeycloakUsername");
IResourceBuilder<ParameterResource> keycloakPassword = builder.AddParameter("KeycloakPassword", secret: true);

IResourceBuilder<ParameterResource> configUrlForWrklWeb =
    builder.AddParameter("config-url-for-project-workerly-web", true);

IResourceBuilder<AzureStorageResource> azurite = builder.AddAzureStorage("stcvsidxlocal")
    .RunAsEmulator(config => config
        .WithBlobPort(27000)
        .WithQueuePort(27001)
        .WithTablePort(27002)
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent));
IResourceBuilder<AzureBlobStorageResource> blobsResource = azurite.AddBlobs("blobs");

IResourceBuilder<AzureCosmosDBResource> cosmos = builder
    .AddAzureCosmosDB("cosmos-cvs-idx-local")
    .RunAsEmulator(emulator =>
    {
        emulator.WithGatewayPort(5005);
        emulator.WithDataVolume();
        emulator.WithLifetime(ContainerLifetime.Persistent);
    });

IResourceBuilder<SqlServerServerResource> sql = builder
    .AddSqlServer("sql-cvs-idx-local", sqlPassword, port: 1433)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

IResourceBuilder<SqlServerDatabaseResource> keycloakDb = sql.AddDatabase("sqldb-cvs-idx-keycloak");

IResourceBuilder<ContainerResource> keycloakOtelCollector = builder
    .AddContainer("ca-cvs-idx-keycloak-otel-collector-local", image: "otel/opentelemetry-collector-contrib:0.136.0")
    .WithBindMount(
        await keycloakOtelCollectorConfigFullPath.Resource.GetValueAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Keycloak OTEL Collector Config Full Path not found."), 
        "/etc/otelcol/config.yaml",
        isReadOnly: true)
    .WithBindMount(
        await keycloakLogOutputFullPath.Resource.GetValueAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Keycloak Log Output Full Path not found."),
        "/var/log/keycloak/keycloak.log",
        isReadOnly: true)
    .WithEnvironment("ASPIRE_OTLP_API_KEY", builder.Configuration["AppHost:OtlpApiKey"])
    .WithArgs("--config", "/etc/otelcol/config.yaml")
    .WithEndpoint(name: "otlp-grpc", port: 4317, targetPort: 4317)
    .WithHttpEndpoint(name: "otlp-http", port: 4318, targetPort: 4318);

IResourceBuilder<KeycloakResource> keycloak = builder
    .AddKeycloak("ca-cvs-idx-keycloak-local", port: 5010, keycloakUsername, keycloakPassword)
    .WithDataVolume()
    .WithBindMount(
        await keycloakLogOutputFullPath.Resource.GetValueAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("Keycloak Log Output Full Path not found."),
        "/var/log/keycloak/keycloak.log",
        isReadOnly: false)
    .WithEnvironment("KC_DB", "mssql")
    .WithEnvironment("KC_DB_USERNAME", "sa")
    .WithEnvironment("KC_DB_PASSWORD", sql.Resource.PasswordParameter)
    .WithEnvironment("KC_DB_URL", () =>
    {
        var host = sql.Resource.Name;
        var port = sql.Resource.PrimaryEndpoint.Port;
        var dbName = keycloakDb.Resource.DatabaseName;
        
        return $"jdbc:sqlserver://{host}:{port};databaseName={dbName};encrypt=false;trustServerCertificate=true;";
    })
    .WithEnvironment("KC_HTTP_ENABLED", "true")
    .WithEnvironment("KC_HOSTNAME_STRICT", "false")
    .WithEnvironment("KC_TRACING_ENABLED", "true")
    .WithEnvironment("KC_TRACING_PROTOCOL", "grpc")
    .WithEnvironment("KC_TRACING_ENDPOINT", () => $"http://{keycloakOtelCollector.Resource.Name}:4317")
    .WithEnvironment("KC_TRACING_SERVICE_NAME", "ca-cvs-idx-keycloak-local")
    .WithEnvironment("KC_TRACING_SAMPLER_RATIO", "1.0")
    .WithEnvironment("KC_METRICS_ENABLED", "true")
    .WithEnvironment("KC_LOG", "file")
    .WithEnvironment("KC_LOG_FILE", "/var/log/keycloak/keycloak.log")
    .WithEnvironment("KC_LOG_FILE_OUTPUT", "json")
    .WithEnvironment("KC_LOG_LEVEL", "info")
    .WithArgs("start-dev --auto-build")
    .WaitFor(keycloakDb)
    .WaitFor(keycloakOtelCollector);

builder.AddProject<Projects.CabaVS_Workerly_Web>("ca-cvs-idx-workerly-local")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("CVS_CONFIGURATION_FROM_AZURE_URL", configUrlForWrklWeb)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithReference(cosmos).WaitFor(cosmos)
    .WithReference(blobsResource, "BlobStorage").WaitFor(blobsResource);

await builder.Build().RunAsync();
