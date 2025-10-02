using Azure.Monitor.OpenTelemetry.Exporter;
using CabaVS.Workerly.Web.Configuration;
using CabaVS.Workerly.Web.Endpoints;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Configuration
builder.Services.Configure<AzureDevOpsOptions>(
    builder.Configuration.GetSection("AzureDevOps"));
builder.Services.Configure<TeamsDefinitionOptions>(
    builder.Configuration.GetSection("TeamsDefinition"));

// Logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Host.UseSerilog();

// Open Telemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(_ => ResourceBuilder.CreateDefault())
    .WithMetrics(metrics =>
    {
        MeterProviderBuilder meterProviderBuilder = metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();
        if (builder.Environment.IsDevelopment())
        {
            meterProviderBuilder.AddOtlpExporter();
        }
        else
        {
            meterProviderBuilder.AddAzureMonitorMetricExporter();
        }
    })
    .WithTracing(tracing =>
    {
        TracerProviderBuilder tracerProviderBuilder = tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (builder.Environment.IsDevelopment())
        {
            tracerProviderBuilder.AddOtlpExporter();
        }
        else
        {
            tracerProviderBuilder.AddAzureMonitorTraceExporter();
        }
    });

// Services
builder.Services.AddSingleton(sp =>
{
    AzureDevOpsOptions options = sp.GetRequiredService<IOptions<AzureDevOpsOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.AccessToken))
    {
        throw new InvalidOperationException("Access Token is not configured.");
    }

    if (string.IsNullOrWhiteSpace(options.OrganizationUrl))
    {
        throw new InvalidOperationException("Organization URL is not configured.");
    }

    var credentials = new VssBasicCredential(string.Empty, options.AccessToken);
    var connection = new VssConnection(new Uri(options.OrganizationUrl), credentials);

    WorkItemTrackingHttpClient? client = connection.GetClient<WorkItemTrackingHttpClient>();
    return client ?? throw new InvalidOperationException("Failed to create WorkItemTrackingHttpClient.");
});

WebApplication app = builder.Build();

app.MapReportingInfoEndpoint();
app.MapRemainingWorkEndpoint();

await app.RunAsync();
