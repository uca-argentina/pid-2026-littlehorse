namespace DrinkIt.Application.Common;

/// <summary>
/// The wall clock of the venue, for the rules written in local hours
/// (promotions, US-54).
/// </summary>
public static class VenueClock
{
    // ponytail: one country. A venue in another timezone needs this to become
    // a column of Venue, read through ICurrentVenue; every caller already goes
    // through Local, so that is the only place to change.
    private const string TimeZoneId = "America/Argentina/Buenos_Aires";

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>The local date and time at a server instant. The server runs in UTC.</summary>
    public static DateTime Local(DateTimeOffset moment) =>
        TimeZoneInfo.ConvertTime(moment, Zone).DateTime;
}
