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
using OpenTelemetry.Exporter;
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

// Shared resources for OTEL metrics and tracing
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(GlobalData.ApplicationName, serviceVersion: GlobalData.ApplicationVersion)
    .AddTelemetrySdk()
    .AddAttributes(new Dictionary<string, object>
    {
        ["host.name"] = Environment.MachineName,
        ["os.description"] = RuntimeInformation.OSDescription,
        ["deployment.environment"] = builder.Environment.EnvironmentName.ToLowerInvariant(),
    });

// Configure logging
builder.Logging.AddOpenTelemetry(loggerOptions =>
{
    loggerOptions.IncludeFormattedMessage = loggerOptions.IncludeScopes = true;
    loggerOptions
        // add rich tags to our logs
        .SetResourceBuilder(resourceBuilder)
        // send logs to OTLP endpoint
        .AddOtlpExporter();
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
        .AddJaegerExporter();
});

// Inject the tracer that we can use inside the application to write spans
//builder.Services.AddSingleton(TracerProvider.Default.GetTracer($"vote-app.{serviceName}"));

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
        // Use this to
        meterProviderBuilder.AddOtlpExporter(otlpOptions =>
        {
            otlpOptions.Endpoint = new(builder.Configuration.GetConnectionString("OTLPEndpoint")!);
            otlpOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
        });
    }
});

builder.Services.AddHttpClient<VoteDataClient>(client =>
    client.BaseAddress = new(builder.Configuration.GetConnectionString("VoteDataServiceUrl")!));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Enable the /metrics endpoint which will be scraped by Prometheus
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub(
    options => options.WebSockets.CloseTimeout = options.LongPolling.PollTimeout = TimeSpan.FromMinutes(10));
app.MapFallbackToPage("/_Host");

app.Run();