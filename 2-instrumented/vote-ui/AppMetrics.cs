using System.Diagnostics.Metrics;
using OpenTelemetry.Trace;

namespace VoteUI;

public class AppMetrics
{
    public AppMetrics(string meterName, string meterVersion)
    {
        var meter = new Meter(meterName, meterVersion);
        VoteCounter = meter.CreateCounter<long>("vote", "count", "Number of votes cast");
    }

    public Counter<long> VoteCounter { get; }
}