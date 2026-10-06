using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading.Tasks;
using Azure;

namespace Bagatka.Azure.Sandboxes;

// One activity and one duration measurement per client call (SandboxesDiagnostics). Static, as .NET
// libraries' activity sources and meters are: listeners subscribe by name.
internal static class Telemetry
{
    private static readonly string? Version = typeof(Telemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    private static readonly ActivitySource Source = new ActivitySource(SandboxesDiagnostics.Name, Version);
    private static readonly Meter Meter = new Meter(SandboxesDiagnostics.Name, Version);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "bagatka.azure.sandboxes.client.operation.duration",
        unit: "s",
        description: "How long a call to Azure Container Apps Sandboxes took");

    public static async Task<T> TraceAsync<T>(string operation, Uri endpoint, Func<Task<T>> call)
    {
        using Activity? activity = Source.StartActivity("SandboxGroupClient." + operation, ActivityKind.Client);
        activity?.SetTag("az.namespace", "Microsoft.App");
        activity?.SetTag("server.address", endpoint.Host);
        long started = Stopwatch.GetTimestamp();
        string? errorType = null;
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errorType = exception is RequestFailedException { ErrorCode: { Length: > 0 } code } ? code : exception.GetType().FullName;
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag("error.type", errorType);
            throw;
        }
        finally
        {
            TagList tags = new TagList { { "operation", operation } };
            if (errorType is not null)
            {
                tags.Add("error.type", errorType);
            }

            Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }
}
