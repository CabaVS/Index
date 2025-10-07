using Azure.Monitor.OpenTelemetry.Exporter;
using CabaVS.Workerly.Web.Configuration;
using CabaVS.Workerly.Web.Endpoints;
using CabaVS.Workerly.Web.Extensions;
using CabaVS.Workerly.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
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

// Logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Host.UseSerilog();

// Configuration
builder.Services.Configure<AzureDevOpsOptions>(
    builder.Configuration.GetSection("AzureDevOps"));
builder.Services.Configure<TeamsDefinitionOptions>(
    builder.Configuration.GetSection("TeamsDefinition"));

// Azure Cosmos DB
builder.TryConfigureCosmosDbForLocalDevelopment();
builder.Services.AddCosmos(builder.Configuration);

// Auth
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.ClientId = builder.Configuration["Authentication:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:ClientSecret"];
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        options.RequireHttpsMetadata = builder.Configuration.GetValue<bool>("Authentication:RequireHttpsMetadata");
        
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
    });
builder.Services .AddAuthorization(
    options => options.FallbackPolicy = new AuthorizationPolicyBuilder() 
        .RequireAuthenticatedUser()
        .Build());

// Razor Pages
builder.Services
    .AddRazorPages(options =>
    {
        options.Conventions.AllowAnonymousToPage("/Index");
        options.Conventions.AllowAnonymousToPage("/Error");
        options.Conventions.AllowAnonymousToPage("/Auth/Login");
    })
    .AddRazorPagesOptions(_ => { });

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

builder.Services.AddScoped<UserService>();

builder.Services.AddHttpContextAccessor();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.MapReportingInfoEndpoint();
app.MapRemainingWorkEndpoint();

await app.RunAsync();
