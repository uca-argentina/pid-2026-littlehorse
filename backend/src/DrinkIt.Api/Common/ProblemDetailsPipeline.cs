namespace DrinkIt.Api.Common;

internal static class ProblemDetailsPipeline
{
    public static IServiceCollection AddProblemDetailsForEveryError(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = NameTheAuthenticationProblem);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    /// <summary>
    /// JwtBearer answers 401 with no body, so without this every unauthenticated
    /// request is indistinguishable from a rejected login once it reaches the
    /// PWA. ADR-0008 leaves us with a single token and no refresh, which makes
    /// an expired session an ordinary end-of-shift event worth its own message.
    /// </summary>
    private static void NameTheAuthenticationProblem(ProblemDetailsContext context)
    {
        int status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;

        if (status != StatusCodes.Status401Unauthorized) return;

        // An endpoint that already named its problem knows more than we do here.
        if (ProblemTypes.Owns(context.ProblemDetails.Type)) return;

        bool expired = SessionExpiry.HasExpired(context.HttpContext);

        context.ProblemDetails.Type = ProblemTypes.For(
            expired ? "auth.session_expired" : "auth.authentication_required");
        context.ProblemDetails.Title = expired ? "Session expired" : "Authentication required";
    }

    public static IApplicationBuilder UseProblemDetailsForEveryError(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        return app;
    }
}
