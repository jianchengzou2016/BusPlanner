namespace BusPlanner.Engine.Models;

/// <summary>Real-time operating state reported for a trip.</summary>
public enum RealtimeTripStatus
{
    /// <summary>The trip is operating as scheduled or with reported updates.</summary>
    Scheduled,
    /// <summary>The trip has been canceled.</summary>
    Canceled,
    /// <summary>The trip has been removed from service.</summary>
    Deleted,
    /// <summary>The feed reports another or unrecognized trip state.</summary>
    Other
}

/// <summary>Real-time operating state reported for a stop on a trip.</summary>
public enum RealtimeStopStatus
{
    /// <summary>The vehicle is expected to serve this stop.</summary>
    Scheduled,
    /// <summary>The vehicle will skip this stop.</summary>
    Skipped,
    /// <summary>Prediction data is unavailable from this stop onward.</summary>
    NoData,
    /// <summary>The feed reports another or unrecognized stop state.</summary>
    Other
}

/// <summary>Effect of a real-time service alert.</summary>
public enum RealtimeAlertEffect
{
    /// <summary>No service is available for the affected selection.</summary>
    NoService,
    /// <summary>A detour affects the selected service.</summary>
    Detour,
    /// <summary>The service is operating in a modified form.</summary>
    ModifiedService,
    /// <summary>The affected stop has moved.</summary>
    StopMoved,
    /// <summary>The alert has another or unrecognized effect.</summary>
    Other
}

/// <summary>A real-time arrival or departure prediction for a stop.</summary>
/// <param name="Time">Predicted event instant, when supplied.</param>
/// <param name="DelaySeconds">Delay relative to the schedule, in seconds, when supplied.</param>
public sealed record RealtimeStopEvent(DateTimeOffset? Time, int? DelaySeconds);

/// <summary>A real-time update for a single stop on a trip.</summary>
/// <param name="StopSequence">The stop's sequence number in the trip, when supplied.</param>
/// <param name="StopId">The feed's identifier for this stop, when supplied.</param>
/// <param name="Arrival">Arrival prediction or delay.</param>
/// <param name="Departure">Departure prediction or delay.</param>
/// <param name="Status">Whether the stop is served or has another reported state.</param>
public sealed record RealtimeStopUpdate(
    uint? StopSequence,
    string? StopId,
    RealtimeStopEvent? Arrival,
    RealtimeStopEvent? Departure,
    RealtimeStopStatus Status);

/// <summary>Real-time updates describing one scheduled trip instance.</summary>
/// <param name="TripId">The feed's trip identifier.</param>
/// <param name="StartDate">Local service date identifying the trip instance, when supplied.</param>
/// <param name="StartTime">Start time identifying a frequency-based trip instance, when supplied.</param>
/// <param name="Status">The trip's reported operating status.</param>
/// <param name="Timestamp">When this trip update was generated, when supplied.</param>
/// <param name="TripDelaySeconds">Trip-wide delay in seconds, when supplied.</param>
/// <param name="StopUpdates">Stop-level predictions and statuses for this trip.</param>
/// <param name="RouteId">The feed's route identifier, when supplied.</param>
public sealed record RealtimeTripUpdate(
    string TripId,
    DateOnly? StartDate,
    string? StartTime,
    RealtimeTripStatus Status,
    DateTimeOffset? Timestamp,
    int? TripDelaySeconds,
    IReadOnlyList<RealtimeStopUpdate> StopUpdates,
    string? RouteId = null);

/// <summary>Identifies the route, trip, or stop affected by a service alert.</summary>
/// <param name="RouteId">Affected route identifier, or <see langword="null"/> when unrestricted.</param>
/// <param name="TripId">Affected trip identifier, or <see langword="null"/> when unrestricted.</param>
/// <param name="StopId">Affected stop identifier, or <see langword="null"/> when unrestricted.</param>
public sealed record RealtimeAlertSelector(
    string? RouteId,
    string? TripId,
    string? StopId);

/// <summary>The time window during which an alert is active.</summary>
/// <param name="Start">Inclusive start instant, or <see langword="null"/> when unbounded.</param>
/// <param name="End">Inclusive end instant, or <see langword="null"/> when unbounded.</param>
public sealed record RealtimeActivePeriod(DateTimeOffset? Start, DateTimeOffset? End);

/// <summary>A real-time notice describing an effect on selected services or stops.</summary>
/// <param name="Effect">The operational effect of the alert.</param>
/// <param name="Header">Short passenger-facing alert text, when supplied.</param>
/// <param name="ActivePeriods">Time windows when the alert applies.</param>
/// <param name="Selectors">Routes, trips, and stops affected by the alert.</param>
public sealed record RealtimeServiceAlert(
    RealtimeAlertEffect Effect,
    string? Header,
    IReadOnlyList<RealtimeActivePeriod> ActivePeriods,
    IReadOnlyList<RealtimeAlertSelector> Selectors);

/// <summary>A parsed snapshot of GTFS-Realtime trip updates and service alerts.</summary>
/// <param name="FeedTimestamp">Timestamp of the feed snapshot, when supplied.</param>
/// <param name="TripUpdates">Trip-level and stop-level real-time updates.</param>
/// <param name="Alerts">Active or scheduled service alerts from the feed.</param>
public sealed record RealtimeSnapshot(
    DateTimeOffset? FeedTimestamp,
    IReadOnlyList<RealtimeTripUpdate> TripUpdates,
    IReadOnlyList<RealtimeServiceAlert> Alerts)
{
    /// <summary>Classifies this snapshot based on its timestamp and the permitted age.</summary>
    /// <param name="now">Current instant against which to measure feed age.</param>
    /// <param name="maximumAge">Maximum age at which the feed is considered fresh.</param>
    /// <returns>Fresh, stale, or missing-timestamp status for this snapshot.</returns>
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
