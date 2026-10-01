using System.Text.Json;
using System.Text.Json.Serialization;
using DrinkIt.Application.Kds;
using DrinkIt.Domain.Orders;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Api.Common;

/// <summary>A staff member's role, as the contract names it.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<StaffRoleName>))]
public enum StaffRoleName
{
    Administrator,
    Kds,
    Waiter,
    Cashier,
}

/// <summary>Where an order is, as the customer's phone reads it.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<CustomerOrderStatus>))]
public enum CustomerOrderStatus
{
    AwaitingPayment,
    Paid,
    Queued,
    InPreparation,
    Ready,
    Delivered,
    Canceled,
}

/// <summary>How the customer is paying, as the checkout sends it.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<PaymentMethodName>))]
public enum PaymentMethodName
{
    Digital,
    Cash,
    VipBalance,
}

/// <summary>Which column of the bar's board an order is in.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<KdsOrderStatus>))]
public enum KdsOrderStatus
{
    Queued,
    InPreparation,
    Ready,
}

/// <summary>What a scan at the bar did, or why it did nothing.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<ScanOutcomeName>))]
public enum ScanOutcomeName
{
    Delivered,
    NotReadyYet,
    AlreadyDelivered,
}

/// <summary>
/// Every enum travels by its name and only by one of its names: no number, no
/// "Administrator,Kds" that a plain enum parser would OR into another value.
/// </summary>
/// <remarks>
/// System.Text.Json's own enum reader ORs a comma-separated list even for an
/// enum that is not [Flags], so this refuses any comma before handing the name
/// to it. Its own type and not an attribute argument because [JsonConverter]
/// can only name a type with a parameterless constructor.
/// </remarks>
public sealed class StrictStringEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly JsonStringEnumConverter<TEnum> Names = new(namingPolicy: null, allowIntegerValues: false);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString()!.Contains(',', StringComparison.Ordinal))
            throw new JsonException($"Only one {typeof(TEnum).Name} at a time.");

        return NamesFor(options).Read(ref reader, typeToConvert, options);
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        NamesFor(options).Write(writer, value, options);

    private static JsonConverter<TEnum> NamesFor(JsonSerializerOptions options) =>
        (JsonConverter<TEnum>)Names.CreateConverter(typeof(TEnum), options)!;
}

/// <summary>
/// The domain's enums to the contract's and back, by hand: renaming a state in
/// the domain must not silently rename it on the wire.
/// </summary>
public static class ContractEnums
{
    public static StaffRoleName ToContract(this StaffRole role) => role switch
    {
        StaffRole.Administrator => StaffRoleName.Administrator,
        StaffRole.Kds => StaffRoleName.Kds,
        StaffRole.Waiter => StaffRoleName.Waiter,
        StaffRole.Cashier => StaffRoleName.Cashier,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "A role the contract has no name for."),
    };

    public static StaffRole ToDomain(this StaffRoleName role) => role switch
    {
        StaffRoleName.Administrator => StaffRole.Administrator,
        StaffRoleName.Kds => StaffRole.Kds,
        StaffRoleName.Waiter => StaffRole.Waiter,
        StaffRoleName.Cashier => StaffRole.Cashier,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not a role this venue hands out."),
    };

    public static CustomerOrderStatus ToCustomerStatus(this OrderStatus status) => status switch
    {
        OrderStatus.AwaitingPayment => CustomerOrderStatus.AwaitingPayment,
        OrderStatus.Paid => CustomerOrderStatus.Paid,
        OrderStatus.Queued => CustomerOrderStatus.Queued,
        OrderStatus.InPreparation => CustomerOrderStatus.InPreparation,
        OrderStatus.Ready => CustomerOrderStatus.Ready,
        OrderStatus.Delivered => CustomerOrderStatus.Delivered,
        OrderStatus.Canceled => CustomerOrderStatus.Canceled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A cart never reaches the customer's answers."),
    };

    public static PaymentMethod ToDomain(this PaymentMethodName method) => method switch
    {
        PaymentMethodName.Digital => PaymentMethod.Digital,
        PaymentMethodName.Cash => PaymentMethod.Cash,
        PaymentMethodName.VipBalance => PaymentMethod.VipBalance,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Not a way of paying this venue knows."),
    };

    public static KdsOrderStatus ToKdsStatus(this OrderStatus status) => status switch
    {
        OrderStatus.Queued => KdsOrderStatus.Queued,
        OrderStatus.InPreparation => KdsOrderStatus.InPreparation,
        OrderStatus.Ready => KdsOrderStatus.Ready,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The bar's board never shows an order in this state."),
    };

    public static ScanOutcomeName ToContract(this ScanOutcome outcome) => outcome switch
    {
        ScanOutcome.Delivered => ScanOutcomeName.Delivered,
        ScanOutcome.NotReadyYet => ScanOutcomeName.NotReadyYet,
        ScanOutcome.AlreadyDelivered => ScanOutcomeName.AlreadyDelivered,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A scan outcome the contract has no name for."),
    };
}
