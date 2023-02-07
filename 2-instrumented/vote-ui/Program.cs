using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Blazored.Toast;
using Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
// Add a filter to log warnings and above
builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("*", LogLevel.Warning);
// Set up logging pipeline
builder.Logging.AddOpenTelemetry(loggerOptions =>
{
    loggerOptions.IncludeFormattedMessage = true;
    loggerOptions.IncludeScopes = true;
    loggerOptions.ParseStateValues = true;
    loggerOptions
        // define the resource
        .SetResourceBuilder(resourceBuilder)
        // add custom processor
        .AddProcessor(new CustomLogProcessor())
        // send logs to the console using exporter
        .AddConsoleExporter();
        // send logs to collector if configured
        if (builder.Configuration.GetValue<bool>("EnableOTLPExporter"))
        {
            loggerOptions.AddOtlpExporter(options =>
                options.Endpoint = new($"http://{builder.Configuration["Hosts:OTLP"]!}:4317"));
        }
});

// Configure tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            // Sets span status to ERROR on exception
            .SetErrorStatusOnException()
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
            .AddProcessor(new BatchActivityExportProcessor(new JaegerExporter(new()
            {
                AgentHost = builder.Configuration["Hosts:Jaeger"],
            })));
    })
    // Configure metrics
    .WithMetrics(meterProviderBuilder =>
    {
        meterProviderBuilder
            // add rich tags to our metrics
            .SetResourceBuilder(resourceBuilder)
            // receive metrics from built-in sources
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            // receive metrics from custom sources
            .AddMeter(GlobalData.SourceName)
            // add view to customize output
            // uncomment the following code to enable the view
            //.AddView(instrument =>
            //{
            //    // remove all instruments except vote
            //    if (instrument.Name != "vote")
            //    {
            //        return MetricStreamConfiguration.Drop;
            //    }

            //    if (instrument.Name == "vote")
            //    {
            //        // customize the vote instrument
            //        return new
            //        {
            //            Name = "app_vote", 
            //            // remove all dimensions except host
            //            TagKeys = new[] { "host" },
            //        };
            //    }

            //    return null;
            //})
            // expose metrics in Prometheus exposition format
            .AddPrometheusExporter();
    })
    .StartWithHost();

builder.Services.AddHttpClient<VoteDataClient>(client =>
    client.BaseAddress = new($"http://{builder.Configuration["Hosts:VoteDataService"]!}:8081"));

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
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// Endpoint to record exception
app.MapGet("/exception/", (TracerProvider tracerProvider, ILogger<Program> logger) =>
    {
        var tracer = tracerProvider.GetTracer(GlobalData.SourceName);
        using var span = tracer.StartActiveSpan("Exception span");
        var simulatedException = new ApplicationException("Error processing the request");
        span.RecordException(simulatedException);
        span.SetStatus(Status.Error);
        logger.LogError(simulatedException, "Error logged");
        return Results.Ok();
    })
    .WithName("Exception")
    .Produces(StatusCodes.Status200OK);
app.Run();