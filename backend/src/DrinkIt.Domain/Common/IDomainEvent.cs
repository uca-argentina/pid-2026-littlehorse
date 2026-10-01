namespace DrinkIt.Domain.Common;

/// <summary>
/// Something that happened to an aggregate, for Application to react to
/// (CLAUDE.md's Domain Events + Observer). The domain only ever raises one of
/// these — it never calls a push service, a hub or anything else outside
/// itself directly.
/// </summary>
public interface IDomainEvent;
