namespace BusPlanner.Engine.Models;

public enum RealtimeTripStatus
{
    Scheduled,
    Canceled,
    Deleted,
    Other
}

public enum RealtimeStopStatus
{
    Scheduled,
    Skipped,
    NoData,
    Other
}

public enum RealtimeAlertEffect
{
    NoService,
    Detour,
    ModifiedService,
    StopMoved,
    Other
}

public sealed record RealtimeStopEvent(DateTimeOffset? Time, int? DelaySeconds);

public sealed record RealtimeStopUpdate(
    uint? StopSequence,
    string? StopId,
    RealtimeStopEvent? Arrival,
    RealtimeStopEvent? Departure,
    RealtimeStopStatus Status);

public sealed record RealtimeTripUpdate(
    string TripId,
    DateOnly? StartDate,
    string? StartTime,
    RealtimeTripStatus Status,
    DateTimeOffset? Timestamp,
    int? TripDelaySeconds,
    IReadOnlyList<RealtimeStopUpdate> StopUpdates);

public sealed record RealtimeAlertSelector(
    string? RouteId,
    string? TripId,
    string? StopId);

public sealed record RealtimeActivePeriod(DateTimeOffset? Start, DateTimeOffset? End);

public sealed record RealtimeServiceAlert(
    RealtimeAlertEffect Effect,
    string? Header,
    IReadOnlyList<RealtimeActivePeriod> ActivePeriods,
    IReadOnlyList<RealtimeAlertSelector> Selectors);

public sealed record RealtimeSnapshot(
    DateTimeOffset? FeedTimestamp,
    IReadOnlyList<RealtimeTripUpdate> TripUpdates,
    IReadOnlyList<RealtimeServiceAlert> Alerts)
{
    public FeedFreshness GetFreshness(DateTimeOffset now, TimeSpan maximumAge)
    {
        if (FeedTimestamp is null)
        {
            return FeedFreshness.MissingTimestamp;
        }

        var age = now - FeedTimestamp.Value;
        return age >= TimeSpan.Zero && age <= maximumAge ? FeedFreshness.Fresh : FeedFreshness.Stale;
    }
}

