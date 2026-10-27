namespace BusPlanner.Api.Options;

public sealed class BusPlannerOptions
{
    public string StaticScheduleUrl { get; set; } = "https://gtfsrt.api.translink.com.au/gtfs/SEQ_SCH_GTFS.zip";
    public string TripUpdatesUrl { get; set; } = "https://gtfsrt.api.translink.com.au/api/realtime/SEQ/TripUpdates";
    public string AlertsUrl { get; set; } = "https://gtfsrt.api.translink.com.au/api/realtime/SEQ/Alerts";
    public int RealtimeCacheSeconds { get; set; } = 60;
    public int MaximumRealtimeAgeSeconds { get; set; } = 180;
    public JourneyOptions Journey { get; set; } = new();
}

public sealed class JourneyOptions
{
    public string Label { get; set; } = "Configured journey";
    public string OriginStopCode { get; set; } = string.Empty;
    public string InterchangeArrivalStopCode { get; set; } = string.Empty;
    public string InterchangeBoardingStopCode { get; set; } = string.Empty;
    public string DestinationStopCode { get; set; } = string.Empty;
    public string FirstRouteShortName { get; set; } = string.Empty;
    public string OnwardRouteShortName { get; set; } = string.Empty;
    public int DefaultApproachMinutes { get; set; } = 10;
    public int DefaultTransferMinutes { get; set; } = 4;

    public Dictionary<string, string[]> RealtimeRouteIdPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
