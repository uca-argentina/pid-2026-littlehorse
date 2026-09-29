using DrinkIt.Application.Common;

namespace DrinkIt.Api.Common;

/// <summary>
/// Who made something and who last touched it, and when. Every part is null
/// when it was never recorded.
/// </summary>
/// <remarks>
/// US-30. A row from before the columns existed has no date, and the screen
/// says "sin registro" instead of showing one that was never recorded.
/// </remarks>
public sealed record AuditResponse(
    DateTimeOffset? CreatedAt,
    string? CreatedBy,
    DateTimeOffset? LastModifiedAt,
    string? LastModifiedBy)
{
    public static AuditResponse Of(AuditInfo audit) =>
        new(audit.CreatedAt, audit.CreatedBy, audit.LastModifiedAt, audit.LastModifiedBy);
}
