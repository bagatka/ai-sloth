using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(
            http =>
            {
                // Configure resilience per integration, not globally: retries can duplicate side effects.

                http.AddServiceDiscovery();
            }
        );

        builder.Services.Configure<ServiceDiscoveryOptions>(
            options =>
            {
                options.AllowedSchemes = ["https"];
            }
        );

        return builder;
    }

    private static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(
            logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            }
        );

        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(
                metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation();
                }
            )
            .WithTracing(
                tracing =>
                {
                    tracing
                        .AddSource(builder.Environment.ApplicationName)
                        .AddAspNetCoreInstrumentation(
                            aspNetCoreTraceInstrumentationOptions =>
                                aspNetCoreTraceInstrumentationOptions.Filter =
                                    context =>
                                        !context.Request.Path.StartsWithSegments(
                                            HealthEndpointPath,
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                        &&
                                        !context.Request.Path.StartsWithSegments(
                                            AlivenessEndpointPath,
                                            StringComparison.OrdinalIgnoreCase
                                        )
                    )
                    .AddHttpClientInstrumentation();
                }
            );

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        bool useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
        );

        if (useOtlpExporter)
        {
            builder.Services
                .AddOpenTelemetry()
                .UseOtlpExporter();
        }

        return builder;
    }

    private static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddRequestTimeouts(
            configure:
                static timeouts =>
                    timeouts.AddPolicy("HealthChecks", TimeSpan.FromSeconds(5))
        );

        builder.Services.AddOutputCache(
            configureOptions:
                static caching =>
                    caching.AddPolicy(
                        "HealthChecks",
                        build:
                            static policy =>
                                policy.Expire(TimeSpan.FromSeconds(10))
                    )
        );

        builder.Services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        RouteGroupBuilder healthChecks = app.MapGroup("");

        healthChecks
            .CacheOutput("HealthChecks")
            .WithRequestTimeout("HealthChecks");

        healthChecks.MapHealthChecks("/health");

        healthChecks.MapHealthChecks(
            "/alive",
            new HealthCheckOptions
            {
                Predicate = static r => r.Tags.Contains("live")
            }
        );

        return app;
    }
}
