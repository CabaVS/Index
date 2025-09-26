IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ParameterResource> sqlPassword = builder.AddParameter("SqlPassword", secret: true);
IResourceBuilder<ParameterResource> otelCollectorConfigFullPath = builder.AddParameter("OtelCollectorConfigFullPath", secret: true);
IResourceBuilder<ParameterResource> keycloakUsername = builder.AddParameter("KeycloakUsername");
IResourceBuilder<ParameterResource> keycloakPassword = builder.AddParameter("KeycloakPassword", secret: true);

IResourceBuilder<SqlServerServerResource> sql = builder
    .AddSqlServer("cvs-idx-sql", sqlPassword, port: 1433)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

IResourceBuilder<SqlServerDatabaseResource> keycloakDb = sql.AddDatabase("cvs-idx-keycloak-sqldb");

IResourceBuilder<ContainerResource> otelCollector = builder
    .AddContainer("cvs-idx-otel-collector", image: "otel/opentelemetry-collector:0.104.0")
    .WithBindMount(
        await otelCollectorConfigFullPath.Resource.GetValueAsync(CancellationToken.None)
        ?? throw new InvalidOperationException("OTEL Collector Config Full Path not found."), 
        "/etc/otelcol/config.yaml")
    .WithEnvironment("ASPIRE_OTLP_API_KEY", builder.Configuration["AppHost:OtlpApiKey"])
    .WithArgs("--config", "/etc/otelcol/config.yaml")
    .WithEndpoint(name: "otlp-grpc", port: 4317, targetPort: 4317)
    .WithHttpEndpoint(name: "otlp-http", port: 4318, targetPort: 4318);

_ = builder
    .AddKeycloak("cvs-idx-keycloak-ca", port: 5010, keycloakUsername, keycloakPassword)
    .WithDataVolume()
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
    .WithEnvironment("KC_TRACING_ENDPOINT", () => $"http://{otelCollector.Resource.Name}:4317")
    .WithEnvironment("KC_TRACING_SERVICE_NAME", "cvs-idx-keycloak-ca")
    .WithEnvironment("KC_TRACING_SAMPLER_RATIO", "1.0")
    .WithEnvironment("KC_METRICS_ENABLED", "true")
    .WithArgs("start-dev --auto-build")
    .WaitFor(keycloakDb)
    .WaitFor(otelCollector);

await builder.Build().RunAsync();
