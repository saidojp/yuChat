using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace YuChat.Infrastructure.Security;

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Messages = "messages";
    public const string Typing = "typing";
    public const string Search = "search";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";

                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
                    ? (int)value.TotalSeconds
                    : 30;

                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.io/429",
                    title = "Too Many Requests",
                    status = 429,
                    detail = "Too many requests. Please slow down and retry later.",
                    code = Errors.ErrorCodes.RateLimited,
                    retryAfterSeconds = retryAfter,
                }, cancellationToken);
            };

            options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            options.AddPolicy(RateLimitPolicies.Search, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            options.AddPolicy(RateLimitPolicies.Messages, context => RateLimitPartition.GetSlidingWindowLimiter(
                context.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromSeconds(10),
                    SegmentsPerWindow = 5,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            options.AddPolicy(RateLimitPolicies.Typing, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromSeconds(5),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });

        return services;
    }
}
