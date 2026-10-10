using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Bagatka.ServiceDefaults;

// A trace starts with work: a request served, or a span a job starts for the one thing it works on
// (PATTERNS.md, entry 21). A call out with nothing above it, such as a job's query looking for work,
// starts no trace, so a host nobody uses sends none. Spans inside a trace follow it, as by default.
internal sealed class TracesStartWithWork : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        bool callOut = samplingParameters.Kind == ActivityKind.Client;
        return new SamplingResult(callOut ? SamplingDecision.Drop : SamplingDecision.RecordAndSample);
    }
}
