using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

namespace VoteUI.Data;

public class VoteService
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;
    private readonly AppMetrics _appMetrics;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _contextAccessor;
    private readonly ILogger<VoteService> _logger;
    private readonly Tracer _tracer;
    private readonly VoteDataClient _voteDataClient;

    public VoteService(
        VoteDataClient voteDataClient,
        TracerProvider provider,
        IConfiguration config,
        IHttpContextAccessor contextAccessor,
        AppMetrics appMetrics,
        ILogger<VoteService> logger)
    {
        _voteDataClient = voteDataClient;
        _tracer = provider.GetTracer(GlobalData.SourceName);
        _config = config;
        _contextAccessor = contextAccessor;
        _appMetrics = appMetrics;
        _logger = logger;
    }

    public async Task<(Vote vote1, Vote vote2)> GetVotesAsync()
    {
        var userAgent = _contextAccessor.HttpContext?.Request.Headers.UserAgent;
        using var span = _tracer.StartActiveSpan("Get votes request", SpanKind.Client);
        // Write custom events to the span
        span.AddEvent($"Received get votes request from {userAgent}");
        // Send user defined properties to the Vote Data service as baggage
        Baggage.SetBaggage("ClientUserAgent", userAgent);
        // HttpClient has been instrumented to propagate context
        var response = await _voteDataClient.Client.GetFromJsonAsync<Result>("/vote");
        span.SetStatus(Status.Ok);
        return (response!.Vote1, response.Vote2);
    }

    public void IncrementVote(int candidate)
    {
        _logger.LogInformation("Received vote for candidate {candidate}", candidate);
        var tags = new TagList
        {
            { "user-agent", _contextAccessor.HttpContext?.Request.Headers.UserAgent },
            { "host", _contextAccessor.HttpContext?.Request.Headers.Host.ToString() },
        };
        _appMetrics.VoteCounter.Add(1, tags);

        var userAgent = _contextAccessor.HttpContext?.Request.Headers.UserAgent;
        var host = _contextAccessor.HttpContext?.Request.Headers.Host;

        using var span = _tracer.StartActiveSpan("RabbitMq publish", SpanKind.Producer);
        span.AddEvent($"Received vote from {userAgent}");
        span.SetAttribute("messaging.system", "rabbitmq");
        span.SetAttribute("messaging.destination_kind", "queue");
        span.SetAttribute("messaging.rabbitmq.queue", _config["Queue:Host"]);

        // Prepare baggage for transfer to message consumer
        Baggage.SetBaggage("ClientUserAgent", userAgent);
        Baggage.SetBaggage("ClientHost", host);

        // Refer to RabbitMQ guide for best practices https://www.rabbitmq.com/dotnet-api-guide.html
        var factory = new ConnectionFactory { HostName = _config["Queue:Host"] };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        var props = channel.CreateBasicProperties();
        // add trace context to message headers
        Propagator.Inject(new(span.Context, Baggage.Current), props, (basicProps, key, value) =>
        {
            basicProps.Headers ??= new Dictionary<string, object>();
            basicProps.Headers[key] = value;
        });

        channel.QueueDeclare(_config["Queue:Name"], autoDelete: false, exclusive: false);
        channel.BasicPublish(string.Empty, "votes", body: BitConverter.GetBytes(candidate), basicProperties: props);
        span.SetStatus(Status.Ok);
        _logger.LogInformation("Published message on queue for candidate {candidate}", candidate);
    }

    public async Task ResetVotesAsync()
    {
        var userAgent = _contextAccessor.HttpContext?.Request.Headers.UserAgent;
        using var span = _tracer.StartActiveSpan("Reset votes request", SpanKind.Client);
        span.AddEvent($"Received reset request from {userAgent}");
        // Send user defined properties to the Vote Data service as baggage
        Baggage.SetBaggage("ClientUserAgent", userAgent);
        await _voteDataClient.Client.PostAsync("/vote/reset", null);
        span.SetStatus(Status.Ok);
        _logger.LogWarning("Votes reset at {time} by {ua}", DateTimeOffset.Now, userAgent);
    }
}