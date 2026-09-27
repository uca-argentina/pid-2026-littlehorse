using DrinkIt.Domain.Common;

namespace DrinkIt.Application.Common;

/// <summary>
/// What a screen shows about who made something and who last touched it
/// (US-30). Every part can be null and each null means something different, so
/// the screen has to be told apart: no creation time is a row from before the
/// columns existed ("sin registro"), no author with a time is a row nobody
/// signed in wrote.
/// </summary>
public sealed record AuditInfo(
    DateTimeOffset? CreatedAt,
    string? CreatedBy,
    DateTimeOffset? LastModifiedAt,
    string? LastModifiedBy)
{
    public static AuditInfo Of(IAudited audited) =>
        new(audited.CreatedAt, audited.CreatedBy, audited.LastModifiedAt, audited.LastModifiedBy);
}
