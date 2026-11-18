using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace YuChat.Infrastructure.Errors;

/// <summary>Machine-readable error codes returned in <c>ProblemDetails.extensions.code</c>.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string InvalidCredentials = "invalid_credentials";
    public const string LoginTaken = "login_taken";
    public const string HandleTaken = "handle_taken";
    public const string HandleRequired = "handle_required";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string UserNotFound = "user_not_found";
    public const string SelfAction = "self_action_not_allowed";
    public const string NotFriends = "not_friends";
    public const string AlreadyFriends = "already_friends";
    public const string RequestPending = "request_pending";
    public const string AlreadyMember = "already_member";
    public const string InvitationPending = "invitation_pending";
    public const string InvalidRefreshToken = "invalid_refresh_token";
    public const string RateLimited = "rate_limited";
    public const string Unsupported = "unsupported_operation";
}

public sealed class AppException(int statusCode, string code, string message, object? details = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public object? Details { get; } = details;

    public static AppException BadRequest(string code, string message, object? details = null) =>
        new(StatusCodes.Status400BadRequest, code, message, details);

    public static AppException Unauthorized(string code, string message) =>
        new(StatusCodes.Status401Unauthorized, code, message);

    public static AppException Forbidden(string message = "You do not have access to this resource.") =>
        new(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden, message);

    public static AppException NotFound(string message = "Resource not found.", string code = ErrorCodes.NotFound) =>
        new(StatusCodes.Status404NotFound, code, message);

    public static AppException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);
}

public static class ProblemDetailsExtensions
{
    public static IResult ToProblem(this AppException exception) =>
        Results.Problem(
            statusCode: exception.StatusCode,
            title: TitleFor(exception.StatusCode),
            detail: exception.Message,
            extensions: BuildExtensions(exception.Code, exception.Details));

    public static IResult Problem(int statusCode, string code, string detail, object? details = null) =>
        Results.Problem(
            statusCode: statusCode,
            title: TitleFor(statusCode),
            detail: detail,
            extensions: BuildExtensions(code, details));

    private static Dictionary<string, object?> BuildExtensions(string code, object? details)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };

        if (details is not null)
        {
            extensions["details"] = details;
        }

        return extensions;
    }

    internal static string TitleFor(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        429 => "Too Many Requests",
        >= 500 => "Server Error",
        _ => "Request Failed",
    };
}

/// <summary>Maps exceptions to RFC 7807 responses and hides internals from clients.</summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var (statusCode, code, detail, details) = exception switch
        {
            AppException app => (app.StatusCode, app.Code, app.Message, app.Details),
            OperationCanceledException => (StatusCodes.Status499ClientClosedRequest, "request_cancelled", "The request was cancelled.", null),
            BadHttpRequestException bad => (StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, bad.Message, null),
            _ => (StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred.", null),
        };

        if (statusCode >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ProblemDetailsExtensions.TitleFor(statusCode),
            Detail = detail,
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (details is not null)
        {
            problem.Extensions["details"] = details;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
