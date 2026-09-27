namespace DrinkIt.Domain.Common;

/// <summary>
/// The moment a row was made, for what the system and customers write: the
/// venue and the orders. There is no "who" here — a customer has no account, and
/// inventing a user to fill the column would be worse than leaving it out.
/// </summary>
/// <remarks>
/// Written by the persistence layer when the row is saved, never by the domain:
/// no use case can forget it, and no test has to set it. Protected so a
/// subclass that already decides its own creation time can say so.
/// </remarks>
public abstract class CreationStamp : IHasCreationTime
{
    public DateTimeOffset? CreatedAt { get; protected set; }
}

/// <summary>The stamp for what an administrator makes and edits. See <see cref="IAudited"/>.</summary>
public abstract class AuditStamps : CreationStamp, IAudited
{
    public string? CreatedBy { get; private set; }

    public DateTimeOffset? LastModifiedAt { get; private set; }

    public string? LastModifiedBy { get; private set; }
}
