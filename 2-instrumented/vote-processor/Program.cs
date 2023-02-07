using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Exporter;
using OpenTelemetry.Extensions.Docker.Resources;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;

IConfiguration config = new ConfigurationBuilder().AddJsonFile("appsettings.json").AddEnvironmentVariables().Build();

// Redis connection
var redisConnection = ConnectionMultiplexer.Connect(config["Hosts:Redis"]);
var redis = redisConnection.GetDatabase();

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
    });

// Configure tracing
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    // Sets span status to ERROR on exception
    .SetErrorStatusOnException()
    .SetResourceBuilder(resourceBuilder)
    // receive traces from our own custom sources
    .AddSource(GlobalData.SourceName)
    .AddRedisInstrumentation(redisConnection, opt => opt.SetVerboseDatabaseStatements = true)
    // ensures that all spans are recorded and sent to exporter
    .SetSampler(new AlwaysOnSampler())
    // stream traces to the SpanExporter
    .AddProcessor(new BatchActivityExportProcessor(new JaegerExporter(new() { AgentHost = config["Hosts:Jaeger"] })))
    .Build();

var tracer = TracerProvider.Default.GetTracer(GlobalData.SourceName, GlobalData.ApplicationVersion);

// Configure logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddOpenTelemetry(loggerOptions =>
    {
        loggerOptions.IncludeFormattedMessage = loggerOptions.IncludeScopes = true;
        loggerOptions
            // add rich tags to our logs
            .SetResourceBuilder(resourceBuilder)
            // send logs to OTLP endpoint
            .AddOtlpExporter();
    });
});

// Set up propagator to extract context from RabbitMq message
var propagator = Propagators.DefaultTextMapPropagator;

var factory = new ConnectionFactory { HostName = config["Queue:Host"], AutomaticRecoveryEnabled = true };
var connection = factory.CreateConnection();
using var channel = connection.CreateModel();
channel.QueueDeclare(config["Queue:Name"], autoDelete: false, exclusive: false);
var consumer = new EventingBasicConsumer(channel);

consumer.Received += async (_, eventArgs) =>
{
    // Extract context from the propagator
    var parentContext = propagator.Extract(default, eventArgs.BasicProperties, (basicProps, key) =>
    {
        if (!basicProps.Headers.TryGetValue(key, out var value))
        {
            return Enumerable.Empty<string>();
        }

        var bytes = value as byte[];
        return new[] { Encoding.UTF8.GetString(bytes ?? Array.Empty<byte>()) };
    });

    // Create child span from the extracted parent context
    using var span = tracer.StartActiveSpan("RabbitMq receive", SpanKind.Consumer,
        new SpanContext(parentContext.ActivityContext));
    // Copy baggage from the parent to the child span
    Baggage.Current = parentContext.Baggage;

    // Set baggage content as attributes on the span
    foreach (var (key, value) in Baggage.Current)
    {
        span.SetAttribute(key, value);
    }

    // Process the message
    var body = eventArgs.Body.ToArray();
    var candidate = BitConverter.ToInt32(body);
    var __ = candidate switch
    {
        1 => await redis.StringIncrementAsync(CacheKeys.Vote1Key),
        2 => await redis.StringIncrementAsync(CacheKeys.Vote2Key),
        _ => throw new UnreachableException(),
    };
};

channel.BasicConsume(config["Queue:Name"], true, consumer);

// Prevent main thread from exiting.
var mre = new ManualResetEvent(false);
mre.WaitOne();