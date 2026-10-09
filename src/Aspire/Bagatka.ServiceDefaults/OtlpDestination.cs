using System;

namespace Bagatka.ServiceDefaults;

/// <summary>
/// Where a host sends its logs, traces, and metrics, over OTLP/HTTP with protobuf.
/// </summary>
/// <param name="BaseAddress">The address each signal's path follows, such as <c>v1/logs</c>.</param>
/// <param name="Headers">Headers for every request, as OTLP writes them: <c>name=value</c>, comma-separated, such as an Authorization.</param>
public sealed record OtlpDestination(Uri BaseAddress, string Headers);
