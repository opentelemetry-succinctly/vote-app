using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Blazored.Toast;
using Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using VoteUI;
using VoteUI.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddBlazoredToast();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<VoteService>();
builder.Services.AddSingleton(new AppMetrics(GlobalData.SourceName, GlobalData.ApplicationVersion));

// Shared resources for OTEL signals
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(GlobalData.ApplicationName, serviceVersion: GlobalData.ApplicationVersion)
    .AddTelemetrySdk()
    .AddAttributes(new Dictionary<string, object>
    {
        
        [ResourceSemanticConventions.AttributeHostName] = Environment.MachineName,
        [ResourceSemanticConventions.AttributeOsDescription] = RuntimeInformation.OSDescription,
        [ResourceSemanticConventions.AttributeDeploymentEnvironment] = builder.Environment.EnvironmentName.ToLowerInvariant(),
    });

// Configure logging
builder.Logging.AddOpenTelemetry(loggerOptions =>
{
    loggerOptions.IncludeFormattedMessage = loggerOptions.IncludeScopes = true;
    loggerOptions
        // add rich tags to our logs
        .SetResourceBuilder(resourceBuilder)
        // send logs to OTLP endpoint
        .AddConsoleExporter();
});

// Configure tracing
builder.Services.AddOpenTelemetryTracing(tracerProviderBuilder =>
{
    tracerProviderBuilder
        // add rich tags to our traces
        .SetResourceBuilder(resourceBuilder)
        // receive traces from our own custom sources
        .AddSource(GlobalData.SourceName)
        // receive traces from built-in sources
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // Ensures that all activities are recorded and sent to exporter
        .SetSampler(new AlwaysOnSampler())
        // send traces to Jaeger
        .AddJaegerExporter(options => options.AgentHost = builder.Configuration["Hosts:Jaeger"]!);

    if (builder.Configuration.GetValue<bool>("EnableOTLPExporter"))
    {
        // Sends metrics to an OTLP endpoint.
        // Use this to send traces to the OTEL collector.
        tracerProviderBuilder.AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = GlobalData.GetOtlpTracesExporterEndpoint(builder.Configuration["Hosts:OTLP"]!);
        });
    }
});

// Configure metrics
builder.Services.AddOpenTelemetryMetrics(meterProviderBuilder =>
{
    meterProviderBuilder
        // add rich tags to our metrics
        .SetResourceBuilder(resourceBuilder)
        // receive metrics from built-in sources
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // receive metrics from custom sources
        .AddMeter(GlobalData.SourceName)
        // expose metrics in Prometheus exposition format
        .AddPrometheusExporter();

    if (builder.Configuration.GetValue<bool>("EnableOTLPExporter"))
    {
        // Sends metrics to an OTLP endpoint.
        // Use this to send traces to the OTEL collector.
        meterProviderBuilder.AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = GlobalData.GetOtlpMetricsExporterEndpoint(builder.Configuration["Hosts:OTLP"]!);
        });
    }
});

builder.Services.AddHttpClient<VoteDataClient>(client =>
    client.BaseAddress = new($"http://{builder.Configuration["Hosts:VoteDataService"]!}:8081"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Add environment information to logs
app.UseMiddleware<LogEnrichmentMiddleware>();

// Enable the /metrics endpoint which will be scraped by Prometheus
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();