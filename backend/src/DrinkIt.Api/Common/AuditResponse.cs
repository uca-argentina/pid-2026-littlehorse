using DrinkIt.Application.Common;

namespace DrinkIt.Api.Common;

/// <summary>
/// Who made something and who last touched it, as the administration screens
/// read it (US-30). Every part travels as null when there is none: a row from
/// before the columns existed has no date, and the screen says "sin registro"
/// instead of showing one that was never recorded.
/// </summary>
public sealed record AuditResponse(
    DateTimeOffset? CreatedAt,
    string? CreatedBy,
    DateTimeOffset? LastModifiedAt,
    string? LastModifiedBy)
{
    public static AuditResponse Of(AuditInfo audit) =>
        new(audit.CreatedAt, audit.CreatedBy, audit.LastModifiedAt, audit.LastModifiedBy);
}
