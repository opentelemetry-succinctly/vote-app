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
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Extensions.Docker.Resources;
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
    // add attributes for the name and version of the service
    .AddService(GlobalData.ApplicationName, serviceVersion: GlobalData.ApplicationVersion)
    // add attributes for the OpenTelemetry SDK version
    .AddTelemetrySdk()
    // Populate platform details
    .AddDetector(new DockerResourceDetector())
    // add custom attributes
    .AddAttributes(new Dictionary<string, object>
    {
        [ResourceSemanticConventions.AttributeHostName] = Environment.MachineName,
        [ResourceSemanticConventions.AttributeOsDescription] = RuntimeInformation.OSDescription,
        [ResourceSemanticConventions.AttributeDeploymentEnvironment] =
            builder.Environment.EnvironmentName.ToLowerInvariant(),
    });

// Configure logging
builder.Logging.AddOpenTelemetry(loggerOptions =>
{
    loggerOptions.IncludeFormattedMessage = loggerOptions.IncludeScopes = true;
    loggerOptions
        // define the resource
        .SetResourceBuilder(resourceBuilder)
        // send logs to OTLP endpoint
        .AddConsoleExporter();
});

// Configure tracing
builder.Services.AddOpenTelemetry().WithTracing(tracerProviderBuilder =>
{
    tracerProviderBuilder
        // define the resource
        .SetResourceBuilder(resourceBuilder)
        // receive traces from our own custom sources
        .AddSource(GlobalData.SourceName)
        // receive traces from built-in sources
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        // ensures that all spans are recorded and sent to exporter
        .SetSampler(new AlwaysOnSampler())
        // stream traces to the SpanExporter
        // BatchActivityExportProcessor processes spans on a separate thread unlike the SimpleActivityExportProcessor
        .AddProcessor(new BatchActivityExportProcessor(
            // Select between Jaeger or OTLP SpanExporter
            builder.Configuration.GetValue<bool>("EnableOTLPExporter")
                // Sends metrics to an OTLP endpoint.
                // Use this to send traces to the OTEL collector.
                ? new OtlpTraceExporter(new()
                {
                    Endpoint = GlobalData.GetOtlpTracesExporterEndpoint(builder.Configuration["Hosts:OTLP"]!),
                })
                : new JaegerExporter(new() { AgentHost = builder.Configuration["Hosts:Jaeger"] })));
});

// Configure metrics
builder.Services.AddOpenTelemetry().WithMetrics(meterProviderBuilder =>
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