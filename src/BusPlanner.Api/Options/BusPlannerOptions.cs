namespace BusPlanner.Api.Options;

public sealed class BusPlannerOptions
{
    public string StaticScheduleUrl { get; set; } = "https://gtfsrt.api.translink.com.au/gtfs/SEQ_SCH_GTFS.zip";
    public string TripUpdatesUrl { get; set; } = "https://gtfsrt.api.translink.com.au/api/realtime/SEQ/TripUpdates";
    public string AlertsUrl { get; set; } = "https://gtfsrt.api.translink.com.au/api/realtime/SEQ/Alerts";
    public int RealtimeCacheSeconds { get; set; } = 60;
    public int MaximumRealtimeAgeSeconds { get; set; } = 180;
    public Dictionary<string, JourneyProfileOptions> Journeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string[]> RealtimeRouteIdPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class JourneyProfileOptions
{
    public string Label { get; set; } = "Configured journey";
    /// <summary>Stable short value used in shared URLs, for example "churchie-home".</summary>
    public string QuickLink { get; set; } = string.Empty;
    /// <summary>QuickLink of a configured reciprocal journey to offer as a header shortcut.</summary>
    public string ReturnJourneyQuickLink { get; set; } = string.Empty;
    public string OriginStopCode { get; set; } = string.Empty;
    public string DestinationStopCode { get; set; } = string.Empty;
    public int DefaultApproachMinutes { get; set; } = 10;
    public int DefaultTransferMinutes { get; set; } = 4;
    public List<JourneyBranchOptions> Branches { get; set; } = new();
}

public sealed class JourneyBranchOptions
{
    public List<JourneyLegOptions> Legs { get; set; } = new();
}

public sealed class JourneyLegOptions
{
    public string BoardStopCode { get; set; } = string.Empty;
    public string AlightStopCode { get; set; } = string.Empty;
    public string[] AllowedRouteShortNames { get; set; } = Array.Empty<string>();
}
