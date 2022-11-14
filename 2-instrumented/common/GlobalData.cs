using System;
using System.Reflection;

namespace Common;

public static class GlobalData
{
    static GlobalData()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var entryAssemblyName = entryAssembly?.GetName();
        ApplicationName = entryAssemblyName?.Name ?? "vote-app";
        ApplicationVersion = entryAssemblyName?.Version?.ToString() ?? "1.0.0.0";
        SourceName = $"vote-app.{ApplicationName}";
    }

    public static string ApplicationName { get; }

    public static string ApplicationVersion { get; }

    public static string SourceName { get; }

    public static Uri GetOtlpTracesExporterEndpoint(string otlpHost) => new($"http://{otlpHost}:4318/v1/traces");

    public static Uri GetOtlpMetricsExporterEndpoint(string otlpHost) => new($"http://{otlpHost}:4318/v1/metrics");
}