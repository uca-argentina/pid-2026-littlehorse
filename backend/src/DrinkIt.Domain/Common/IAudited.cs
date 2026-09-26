namespace DrinkIt.Domain.Common;

/// <summary>
/// Something that remembers when it was made (US-30). Null on every row that
/// existed before the column did: a date invented for them would say when the
/// migration ran and not when the thing happened, which is the lie this whole
/// story exists to avoid.
/// </summary>
public interface IHasCreationTime
{
    DateTimeOffset? CreatedAt { get; }
}

/// <summary>
/// Something an administrator makes and edits, so it also remembers who did
/// each and when. "Who" is the username of the token that made the request:
/// null when nobody was signed in, and never a made-up user.
/// </summary>
public interface IAudited : IHasCreationTime
{
    string? CreatedBy { get; }

    DateTimeOffset? LastModifiedAt { get; }

    string? LastModifiedBy { get; }
}
