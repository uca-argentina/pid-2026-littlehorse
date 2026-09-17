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
    /// JwtBearer answers 401 with no body and an authorization policy answers
    /// 403 with no body, so without this every one of them is indistinguishable
    /// from a rejected login once it reaches the PWA. ADR-0008 leaves us with a
    /// single token and no refresh, which makes an expired session an ordinary
    /// end-of-shift event worth its own message.
    /// </summary>
    private static void NameTheAuthenticationProblem(ProblemDetailsContext context)
    {
        int status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;

        // An endpoint that already named its problem knows more than we do here.
        if (ProblemTypes.Owns(context.ProblemDetails.Type)) return;

        // Signing in again would change nothing: the token is fine, the role is
        // not the one that screen needs. Telling this apart from a 401 is what
        // keeps the PWA from bouncing a KDS to the login screen for ever.
        if (status == StatusCodes.Status403Forbidden)
        {
            context.ProblemDetails.Type = ProblemTypes.For("auth.forbidden");
            context.ProblemDetails.Title = "Not allowed for this role";

            return;
        }

        if (status != StatusCodes.Status401Unauthorized) return;

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
