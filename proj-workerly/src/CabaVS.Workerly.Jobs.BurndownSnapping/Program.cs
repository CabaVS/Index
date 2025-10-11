using Azure.Monitor.OpenTelemetry.Exporter;
using CabaVS.Common.Infrastructure.ConfigurationProviders;
using CabaVS.Workerly.Jobs.BurndownSnapping;
using CabaVS.Workerly.Jobs.BurndownSnapping.Configuration;
using CabaVS.Workerly.Shared;
using CabaVS.Workerly.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) => config.AddJsonStreamFromBlob(
        context.HostingEnvironment.IsDevelopment()))
    .ConfigureServices((context, services) =>
    {
        services.Configure<RemainingWorkTrackerOptions>(
            context.Configuration.GetSection("RemainingWorkTracker"));
        
        services.AddOpenTelemetry()
            .ConfigureResource(_ => ResourceBuilder.CreateDefault())
            .WithMetrics(metrics =>
            {
                MeterProviderBuilder meterProviderBuilder = metrics
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                if (context.HostingEnvironment.IsDevelopment())
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
                    .AddSource(Constants.ActivityNames.RemainingWorkTracker)
                    .AddHttpClientInstrumentation();
                if (context.HostingEnvironment.IsDevelopment())
                {
                    tracerProviderBuilder.AddOtlpExporter();
                }
                else
                {
                    tracerProviderBuilder.AddAzureMonitorTraceExporter();
                }
            });
        
        services.AddCosmosPersistenceServices(
            context.Configuration,
            context.HostingEnvironment.IsDevelopment(),
            context.HostingEnvironment.EnvironmentName);
        services.TryConfigureCosmosDbForLocalDevelopment(
            context.Configuration,
            context.HostingEnvironment.IsDevelopment());
        
        services.AddScoped<AzureDevOpsIntegrationService>();
        
        services.AddHostedService<Application>();
    })
    .UseSerilog((context, config) => config.ReadFrom.Configuration(context.Configuration))
    .Build();

await host.RunAsync();
