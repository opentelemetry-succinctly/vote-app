using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;

TraceSamplerTest(new AlwaysOnSampler());
TraceSamplerTest(new AlwaysOffSampler());
TraceSamplerTest(new TraceIdRatioBasedSampler(0.5));
Console.ReadLine();


static void TraceSamplerTest(Sampler sampler)
{
    using var alwaysOnBuilder = Sdk.CreateTracerProviderBuilder()
        .AddSource("tracer")
        .SetSampler(sampler)
        .AddConsoleExporter(o => o.Targets = ConsoleExporterOutputTargets.Console)
        .Build();
    using var span = alwaysOnBuilder?.GetTracer("tracer").StartActiveSpan("span");
    span?.SetAttribute("type", sampler.GetType().Name);
}