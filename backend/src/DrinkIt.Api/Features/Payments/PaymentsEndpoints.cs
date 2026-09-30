using DrinkIt.Api.Common;
using DrinkIt.Api.Tenancy;
using DrinkIt.Application.Common;
using DrinkIt.Application.Payments;
using DrinkIt.Domain.Orders;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DrinkIt.Api.Features.Payments;

/// <summary>What the customer's screen sends back from Mercado Pago's page.</summary>
/// <remarks><c>PaymentId</c> is empty when they came back without paying.</remarks>
public sealed record PaymentReturnRequest(string? PaymentId);

/// <summary>What the checkout needs to draw Mercado Pago's own button. Null: use the app's own.</summary>
public sealed record PaymentConfigurationResponse(string? PublicKey);

/// <summary>Where the order is after the customer came back from paying it.</summary>
public sealed record PaymentReturnResponse(CustomerOrderStatus Status);

internal static partial class PaymentsEndpoints
{
    public static IEndpointRouteBuilder MapPayments(this IEndpointRouteBuilder endpoints)
    {
        // The customer's door, like the tracking link: the token in the path
        // is the proof the order is theirs.
        endpoints
            .MapPost("/{venueSlug}/orders/{code}/{token}/payment", ReturnAsync)
            .AllowAnonymous()
            .WithMetadata(new ScopedBySlugAttribute())
            .WithName("ReturnFromPayment")
            .WithTags("Payments")
            .WithSummary("The customer is back from Mercado Pago's page: moves the order by what happened to its payment.")
            .Produces<PaymentReturnResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Mercado Pago's door. The slug is in the address it was given, which
        // is how a notification arrives scoped to a venue.
        endpoints
            .MapPost("/{venueSlug}/payments/notifications", NotifyAsync)
            .AllowAnonymous()
            .WithMetadata(new ScopedBySlugAttribute())
            .WithName("PaymentNotification")
            .WithTags("Payments")
            .WithSummary("A payment notification from Mercado Pago. Only believed when it carries Mercado Pago's signature.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Public by nature and the same for every venue. Never cached: a key
        // rotated in Azure reaches the next phone that asks.
        endpoints
            .MapGet("/payments/configuration", Configuration)
            .AllowAnonymous()
            .WithName("PaymentConfiguration")
            .WithTags("Payments")
            .WithSummary("The public key the checkout draws Mercado Pago's button with, or null when there is none.")
            .Produces<PaymentConfigurationResponse>();

        return endpoints;
    }

    internal static IResult Configuration(IPaymentClientConfiguration configuration, HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store, max-age=0";

        return TypedResults.Ok(new PaymentConfigurationResponse(configuration.PublicKey));
    }

    internal static async Task<IResult> ReturnAsync(
        string code,
        string token,
        PaymentReturnRequest request,
        CurrentVenue venue,
        ReturnFromPaymentHandler handler,
        CancellationToken cancellationToken)
    {
        if (venue.Identity is null) return NotFound(PaymentErrors.OrderNotFound);

        Result<OrderStatus> result = await handler.HandleAsync(code, token, request.PaymentId, cancellationToken);

        if (result.IsSuccess) return TypedResults.Ok(new PaymentReturnResponse(result.Value.ToCustomerStatus()));

        return result.Error!.Code == PaymentErrors.PaidAfterCancel.Code
            ? Problem(result.Error, StatusCodes.Status409Conflict, "Conflict")
            : NotFound(result.Error);
    }

    /// <summary>
    /// 200 for everything that is not a forgery: Mercado Pago retries any other
    /// answer for a day, and a payment of another venue or one that arrived too
    /// late will not become right by being sent again.
    /// </summary>
    internal static async Task<IResult> NotifyAsync(
        [FromQuery(Name = "data.id")] string? dataId,
        [FromQuery(Name = "type")] string? type,
        [FromHeader(Name = "x-signature")] string? signature,
        [FromHeader(Name = "x-request-id")] string? requestId,
        CurrentVenue venue,
        IPaymentNotificationVerifier verifier,
        ApplyPaymentHandler handler,
        ILogger<PaymentReturnRequest> logger,
        CancellationToken cancellationToken)
    {
        if (!verifier.IsGenuine(signature, requestId, dataId))
            return Problem(new Error("payment.notification_not_signed", "That notification is not signed by Mercado Pago."), StatusCodes.Status401Unauthorized, "Unauthorized");

        if (venue.Identity is null || type != "payment" || string.IsNullOrWhiteSpace(dataId)) return TypedResults.Ok();

        Result<OrderStatus> result = await handler.HandleAsync(dataId, cancellationToken);

        if (!result.IsSuccess) LogNotificationNotApplied(logger, dataId, result.Error!.Code);

        return TypedResults.Ok();
    }

    private static ProblemHttpResult NotFound(Error error) =>
        Problem(error, StatusCodes.Status404NotFound, "Not found");

    private static ProblemHttpResult Problem(Error error, int status, string title) =>
        TypedResults.Problem(
            title: title,
            detail: error.Message,
            statusCode: status,
            type: ProblemTypes.For(error.Code));

    // A payment that moved nothing: not this venue's, unknown to Mercado Pago,
    // or approved for an order already canceled — that one has to be refunded.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment {PaymentId} notified but not applied: {ErrorCode}.")]
    private static partial void LogNotificationNotApplied(ILogger logger, string paymentId, string errorCode);
}
