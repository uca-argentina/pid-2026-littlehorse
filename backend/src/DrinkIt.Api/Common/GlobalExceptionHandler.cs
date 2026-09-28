using DrinkIt.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DrinkIt.Api.Common;

/// <summary>
/// Turns anything that escapes an endpoint into an RFC 9457 response: a broken
/// domain invariant into a 400 that names the rule, everything else into a
/// generic 500 whose cause stays in the log.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is DomainException broken) return await RejectAsync(httpContext, broken);
        if (exception is BadHttpRequestException unreadable) return await UnreadableAsync(httpContext, unreadable);

        LogUnhandledException(logger, httpContext.Request.Method, httpContext.Request.Path, exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await WriteAsync(
            httpContext,
            exception,
            // Fixed text, never exception.Message: a SqlException names tables
            // and columns and a DbUpdateException sometimes carries the
            // connection string. The traceId is what ties this response to the
            // log line that does have the real cause.
            detail: "The request could not be completed. Quote the traceId when reporting it.");
    }

    /// <summary>
    /// A request the framework could not read: a body that is not JSON, or a
    /// value that is not one of the contract's — a number where a role name
    /// goes. ASP.NET already decided the status; answering 500 would call the
    /// caller's mistake ours.
    /// </summary>
    /// <remarks>
    /// Fixed text, never the exception's message: it quotes our own type names,
    /// and the parser's wording is not part of the contract.
    /// </remarks>
    private async ValueTask<bool> UnreadableAsync(HttpContext httpContext, BadHttpRequestException unreadable)
    {
        LogUnreadableRequest(logger, httpContext.Request.Method, httpContext.Request.Path, unreadable.StatusCode);

        httpContext.Response.StatusCode = unreadable.StatusCode;

        return await WriteAsync(
            httpContext,
            unreadable,
            detail: "The request could not be read. Check that every value is one the API accepts.",
            title: "Invalid request");
    }

    /// <summary>
    /// A rule the domain guards, broken by what the caller sent. The code
    /// becomes the problem type so the PWA can point at the offending field,
    /// and the message is safe to forward: the domain writes it for us and it
    /// names nothing about the database.
    /// </summary>
    private async ValueTask<bool> RejectAsync(HttpContext httpContext, DomainException broken)
    {
        LogBrokenInvariant(logger, broken.Code, httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        return await WriteAsync(
            httpContext,
            broken,
            detail: broken.Message,
            title: "Invalid request",
            type: ProblemTypes.For(broken.Code));
    }

    private ValueTask<bool> WriteAsync(
        HttpContext httpContext,
        Exception exception,
        string detail,
        string? title = null,
        string? type = null) =>
        problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Detail = detail,
                Title = title,
                Type = type,

                // RFC 9457 §3.1.2 types "instance" as a URI reference, exactly
                // like "type". The path is one; "GET /orders" is not.
                Instance = httpContext.Request.Path,
            },
        });

    // Source-generated because CA1848 is an error here: the boxing and the
    // format parsing of LoggerExtensions.LogError run even when the level is off.
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Unhandled exception while handling {Method} {Path}.")]
    private static partial void LogUnhandledException(
        ILogger logger,
        string method,
        string path,
        Exception exception);

    // Warning and not Error: the request was wrong, the server was not. It is
    // still worth a line, because reaching the domain means it got past both
    // the form and the endpoint's own validation.
    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Rejected {Method} {Path}: the domain rule {Rule} was broken.")]
    private static partial void LogBrokenInvariant(ILogger logger, string rule, string method, string path);

    // Warning, like a broken invariant: the request was wrong, the server was not.
    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Rejected {Method} {Path}: the request could not be read ({Status}).")]
    private static partial void LogUnreadableRequest(ILogger logger, string method, string path, int status);
}
