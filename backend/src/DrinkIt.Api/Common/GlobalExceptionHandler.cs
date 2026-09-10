using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DrinkIt.Api.Common;

/// <summary>
/// Turns anything that escapes an endpoint into a generic RFC 9457 response.
/// The exception itself goes to the log; the caller gets a status, a traceId
/// and nothing else.
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
        LogUnhandledException(logger, httpContext.Request.Method, httpContext.Request.Path, exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                // Fixed text, never exception.Message: a SqlException names
                // tables and columns and a DbUpdateException sometimes carries
                // the connection string. The traceId above is what ties this
                // response to the log line that does have the real cause.
                Detail = "The request could not be completed. Quote the traceId when reporting it.",

                // RFC 9457 §3.1.2 types "instance" as a URI reference, exactly
                // like "type". The path is one; "GET /orders" is not.
                Instance = httpContext.Request.Path,
            },
        });
    }

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
}
