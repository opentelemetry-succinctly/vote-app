using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Extensions.Docker.Resources;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using VoteData;

var builder = WebApplication.CreateBuilder(args);

// Add Redis service
var redisConnection = ConnectionMultiplexer.Connect(builder.Configuration["Hosts:Redis"]);
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

builder.Services.AddSingleton<VoteDataService>();

// Application settings
builder.Services.Configure<VoteSettings>(builder.Configuration.GetSection(nameof(VoteSettings)));

// Shared resources for OTEL signals
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(GlobalData.ApplicationName, serviceVersion: GlobalData.ApplicationVersion)
    .AddTelemetrySdk()
    // Populate platform details
    .AddDetector(new DockerResourceDetector())
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
            .AddProcessor(new BatchActivityExportProcessor(
                // Select between Jaeger or OTLP SpanExporter
                builder.Configuration.GetValue<bool>("EnableOTLPExporter")
                    // Sends metrics to an OTLP endpoint.
                    // Use this to send traces to the OTEL collector.
                    ? new OtlpTraceExporter(
                        new() { Endpoint = new($"http://{builder.Configuration["Hosts:OTLP"]!}:4317") })
                    : new JaegerExporter(new() { AgentHost = builder.Configuration["Hosts:Jaeger"] })));
    })
    .StartWithHost();

var app = builder.Build();

// API Endpoints
var apiGroup = app.MapGroup("/vote").WithDescription("Vote Data API");
apiGroup.MapGet("/", static async (VoteDataService vds) => await vds.GetVotesAsync());
apiGroup.MapPost("/reset", static async (VoteDataService vds) => await vds.ResetVotesAsync());

app.Run();