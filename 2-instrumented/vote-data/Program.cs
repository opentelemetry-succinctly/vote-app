using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using VoteData;

var builder = WebApplication.CreateBuilder(args);

// Add Redis service
var redisConnection = ConnectionMultiplexer.Connect($"{builder.Configuration["Hosts:Redis"]}");
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

builder.Services.AddSingleton<VoteDataService>();

// Application settings
builder.Services.Configure<VoteSettings>(builder.Configuration.GetSection(nameof(VoteSettings)));

// Shared resources for OTEL signals
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(GlobalData.ApplicationName, serviceVersion: GlobalData.ApplicationVersion)
    .AddTelemetrySdk()
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
        .AddRedisInstrumentation(redisConnection, opt => opt.SetVerboseDatabaseStatements = true)
        // send traces to Jaeger
        .AddJaegerExporter(options => options.AgentHost = builder.Configuration["Hosts:Jaeger"]!);
});

// Inject the tracer that we can use inside the application to write spans
builder.Services.AddSingleton(TracerProvider.Default.GetTracer(GlobalData.SourceName, GlobalData.ApplicationVersion));

var app = builder.Build();

// API Endpoints
app.MapGet("/vote", static async (VoteDataService vds) => await vds.GetVotesAsync());
app.MapPost("/vote/reset", static async (VoteDataService vds) => await vds.ResetVotesAsync());

app.Run();