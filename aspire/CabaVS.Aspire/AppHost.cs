IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ParameterResource> sqlPassword = builder.AddParameter("SqlPassword", secret: true);
IResourceBuilder<ParameterResource> keycloakUsername = builder.AddParameter("KeycloakUsername");
IResourceBuilder<ParameterResource> keycloakPassword = builder.AddParameter("KeycloakPassword", secret: true);

IResourceBuilder<SqlServerServerResource> sql = builder
    .AddSqlServer("cvs-idx-sql", sqlPassword, port: 1433)
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

IResourceBuilder<SqlServerDatabaseResource> keycloakDb = sql.AddDatabase("cvs-idx-keycloak-sqldb");

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
    .WithArgs("start-dev --auto-build")
    .WaitFor(keycloakDb);

await builder.Build().RunAsync();
