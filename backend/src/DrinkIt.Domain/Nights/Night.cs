using DrinkIt.Domain.Common;
using DrinkIt.Domain.Staff;

namespace DrinkIt.Domain.Nights;

/// <summary>
/// One night of a venue (US-35): its hours, and the KDS, cashiers and waiters
/// working it. What organises everything else in the night — orders, stock,
/// metrics — because a night crosses midnight and "today" cannot.
/// </summary>
/// <remarks>
/// Two nights of the same venue overlapping is a rule across aggregates, so it
/// is the handler's to check and not this class's.
/// </remarks>
public sealed class Night : AuditStamps, IBelongsToVenue
{
    public static class ErrorCodes
    {
        public const string VenueRequired = "night.venue_required";
        public const string NameRequired = "night.name_required";
        public const string NameLength = "night.name_length";
        public const string HoursInvalid = "night.hours_invalid";
        public const string KdsRequired = "night.kds_required";
        public const string CashierRequired = "night.cashier_required";
        public const string CrewRoleInvalid = "night.crew_role_invalid";
        public const string CrewMemberInactive = "night.crew_member_inactive";
        public const string CrewMemberFromAnotherVenue = "night.crew_member_from_another_venue";
        public const string StartLocked = "night.start_locked";
        public const string NightOver = "night.over";
    }

    public const int NameMaxLength = 60;

    private readonly List<Guid> _crewIds = [];

    private Night(Guid id, Guid venueId, string name, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        Id = id;
        VenueId = venueId;
        Name = name;
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    public Guid Id { get; }

    public Guid VenueId { get; }

    public string Name { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    /// <summary>Exclusive: at this moment the night no longer takes orders.</summary>
    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>The staff accounts working the night. Only ids: their roles stay in <see cref="StaffUser"/>.</summary>
    public IReadOnlyList<Guid> CrewIds => _crewIds;

    public static Night Create(Guid venueId, string name, DateTimeOffset startsAt, DateTimeOffset endsAt, IEnumerable<StaffUser> crew)
    {
        if (venueId == Guid.Empty) throw new DomainException(ErrorCodes.VenueRequired, "Nights must belong to a venue.");

        string cleanName = CleanName(name);
        CheckHours(startsAt, endsAt);
        List<Guid> crewIds = CheckCrew(venueId, crew);

        Night night = new(Guid.CreateVersion7(), venueId, cleanName, startsAt, endsAt);
        night._crewIds.AddRange(crewIds);

        return night;
    }

    /// <summary>
    /// What the night's form saves. An upcoming night changes freely; one
    /// underway keeps its start, because its orders already belong to it, and
    /// an end that already passed closes it now ("acortarla a una hora que ya
    /// pasó es cerrarla"); a finished one is read-only, since its metrics are
    /// read against those hours.
    /// </summary>
    public void Update(string name, DateTimeOffset startsAt, DateTimeOffset endsAt, IEnumerable<StaffUser> crew, DateTimeOffset now)
    {
        if (now >= EndsAt) throw new DomainException(ErrorCodes.NightOver, "The night is over and can no longer change.");

        bool underway = IsUnderwayAt(now);

        if (underway && startsAt != StartsAt) throw new DomainException(ErrorCodes.StartLocked, "The night already started: its start can no longer move.");

        DateTimeOffset effectiveEnd = underway && endsAt <= now ? now : endsAt;

        string cleanName = CleanName(name);
        CheckHours(startsAt, effectiveEnd);
        List<Guid> crewIds = CheckCrew(VenueId, crew);

        Name = cleanName;
        StartsAt = startsAt;
        EndsAt = effectiveEnd;
        _crewIds.Clear();
        _crewIds.AddRange(crewIds);
    }

    public bool IsUnderwayAt(DateTimeOffset moment) => StartsAt <= moment && moment < EndsAt;

    public bool IsWorkedBy(Guid staffUserId) => _crewIds.Contains(staffUserId);

    private static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException(ErrorCodes.NameRequired, "The night needs a name.");

        string cleanName = name.Trim();

        if (cleanName.Length > NameMaxLength) throw new DomainException(ErrorCodes.NameLength, $"The name cannot be longer than {NameMaxLength} characters.");

        return cleanName;
    }

    private static void CheckHours(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        if (endsAt <= startsAt) throw new DomainException(ErrorCodes.HoursInvalid, "The night has to end after it starts.");
    }

    /// <summary>The crew rules, the same for creating and editing. Returns the ids in the order given, once each.</summary>
    private static List<Guid> CheckCrew(Guid venueId, IEnumerable<StaffUser> crew)
    {
        List<StaffUser> members = crew.DistinctBy(member => member.Id).ToList();

        foreach (StaffUser member in members)
        {
            if (member.VenueId != venueId) throw new DomainException(ErrorCodes.CrewMemberFromAnotherVenue, "Every account in the night has to belong to its venue.");
            if (!member.IsActive) throw new DomainException(ErrorCodes.CrewMemberInactive, $"'{member.Username}' is deactivated and cannot work the night.");
            if (member.Role == StaffRole.Administrator) throw new DomainException(ErrorCodes.CrewRoleInvalid, "Administrators are not part of a night's crew: they always have access.");
        }

        if (!members.Any(member => member.Role == StaffRole.Kds)) throw new DomainException(ErrorCodes.KdsRequired, "The night needs at least one KDS.");
        if (!members.Any(member => member.Role == StaffRole.Cashier)) throw new DomainException(ErrorCodes.CashierRequired, "The night needs at least one cashier.");

        return members.Select(member => member.Id).ToList();
    }
}
