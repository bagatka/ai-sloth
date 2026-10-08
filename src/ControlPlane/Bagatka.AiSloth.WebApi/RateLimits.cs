using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.WebApi;

// Bounds what one caller can make the host do, in this instance's memory: sign-ins per address, the
// only thing that tells anonymous callers apart, and starts of chats and nooks per person, as each
// takes compute. A refusal is 429 with Retry-After. The model gateway has none: agents of many nooks
// may share an address, and every call needs its chat's token.
internal static class RateLimits
{
    public const string SignIn = "sign-in";
    public const string Starts = "starts";

    private const int StartsAtOnce = 20;

    public static IServiceCollection AddRateLimits(this IServiceCollection services, HostSettings host)
    {
        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(SignIn, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = host.SignInsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy(Starts, context => RateLimitPartition.GetTokenBucketLimiter(
                PersonOf(context.User),
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = StartsAtOnce,
                    TokensPerPeriod = 1,
                    ReplenishmentPeriod = TimeSpan.FromHours(1) / host.StartsPerHour,
                    QueueLimit = 0,
                }));
            options.OnRejected = async (rejected, ct) =>
            {
                bool known = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter);
                if (known)
                {
                    rejected.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                ProblemDetails problem = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests at once; try again in a moment.",
                    Extensions = { ["code"] = "host.too_many_requests" },
                };
                await TypedResults.Problem(problem).ExecuteAsync(rejected.HttpContext);
            };
        });
    }

    private static string PersonOf(ClaimsPrincipal principal)
    {
        return principal.ToActor() is UserActor user ? user.UserId.Value.ToString("N", CultureInfo.InvariantCulture) : "anonymous";
    }
}
