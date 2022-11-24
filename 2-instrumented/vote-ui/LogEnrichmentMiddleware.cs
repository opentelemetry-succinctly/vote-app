using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace VoteUI;

public class LogEnrichmentMiddleware
{
    private readonly ILogger<LogEnrichmentMiddleware> _logger;
    private readonly RequestDelegate _next;

    public LogEnrichmentMiddleware(RequestDelegate next, ILogger<LogEnrichmentMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var logState = new ReadOnlyCollection<KeyValuePair<string, object>>(new List<KeyValuePair<string, object>>
        {
            new("ProcessID", Environment.ProcessId), 
            new("DotnetFramework", RuntimeInformation.FrameworkDescription),
            new("Runtime", RuntimeInformation.RuntimeIdentifier),
        });
        using var _ = _logger.BeginScope(logState);
        await _next(httpContext);
    }
}