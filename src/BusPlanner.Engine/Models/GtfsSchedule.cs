namespace BusPlanner.Engine.Models;

public sealed record TransitStop(string Id, string Name, string? ParentStationId);

public sealed record TransitRoute(
    string Id,
    string? ShortName,
    string? LongName,
    int? RouteType,
    string? AgencyId);

public sealed record TransitTrip(
    string Id,
    string RouteId,
    string ServiceId,
    string? HeadSign,
    int? DirectionId);

public sealed record TransitStopTime(
    string TripId,
    string StopId,
    int StopSequence,
    TimeSpan? ArrivalTime,
    TimeSpan? DepartureTime,
    bool Timepoint);

public sealed record ServiceCalendar(
    string ServiceId,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlySet<DayOfWeek> OperatingDays);

public sealed record ServiceException(string ServiceId, DateOnly Date, bool Added);

public sealed record TransitFrequency(
    string TripId,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int HeadwaySeconds,
    bool ExactTimes);

public sealed class GtfsSchedule
{
    private readonly Dictionary<string, TransitStopTime[]> _stopTimesByTrip;
    private readonly Dictionary<string, TransitStopTime[]> _stopTimesByStop;
    private readonly Dictionary<string, TransitTrip> _tripsById;
    private readonly Dictionary<string, ServiceCalendar> _calendars;
    private readonly Dictionary<(string ServiceId, DateOnly Date), bool> _exceptions;
    private readonly Dictionary<string, TransitFrequency[]> _frequenciesByTrip;

    internal GtfsSchedule(
        TimeZoneInfo agencyTimeZone,
        IReadOnlyDictionary<string, TransitStop> stops,
        IReadOnlyDictionary<string, TransitRoute> routes,
        IReadOnlyDictionary<string, TransitTrip> trips,
        IEnumerable<TransitStopTime> stopTimes,
        IEnumerable<ServiceCalendar> calendars,
        IEnumerable<ServiceException> exceptions,
        IEnumerable<TransitFrequency> frequencies)
    {
        AgencyTimeZone = agencyTimeZone;
        Stops = stops;
        Routes = routes;
        _tripsById = trips.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        _stopTimesByTrip = stopTimes
            .GroupBy(x => x.TripId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.StopSequence).ToArray(), StringComparer.Ordinal);
        _stopTimesByStop = stopTimes
            .GroupBy(x => x.StopId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.DepartureTime ?? y.ArrivalTime).ToArray(), StringComparer.Ordinal);
        _calendars = calendars.ToDictionary(x => x.ServiceId, StringComparer.Ordinal);
        _exceptions = exceptions.ToDictionary(x => (x.ServiceId, x.Date), x => x.Added);
        _frequenciesByTrip = frequencies
            .GroupBy(x => x.TripId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.StartTime).ToArray(), StringComparer.Ordinal);
    }

    public TimeZoneInfo AgencyTimeZone { get; }
    public IReadOnlyDictionary<string, TransitStop> Stops { get; }
    public IReadOnlyDictionary<string, TransitRoute> Routes { get; }
    public int TripCount => _tripsById.Count;

    internal IReadOnlyList<TransitStopTime> GetStopTimesForTrip(string tripId) =>
        _stopTimesByTrip.TryGetValue(tripId, out var values) ? values : Array.Empty<TransitStopTime>();

    internal IReadOnlyList<TransitStopTime> GetDeparturesAtStop(string stopId) =>
        _stopTimesByStop.TryGetValue(stopId, out var values) ? values : Array.Empty<TransitStopTime>();

    internal bool TryGetTrip(string tripId, out TransitTrip trip) => _tripsById.TryGetValue(tripId, out trip!);

    internal IReadOnlyList<TransitFrequency> GetFrequencies(string tripId) =>
        _frequenciesByTrip.TryGetValue(tripId, out var values) ? values : Array.Empty<TransitFrequency>();

    public bool IsServiceActive(string serviceId, DateOnly date)
    {
        if (_exceptions.TryGetValue((serviceId, date), out var added))
        {
            return added;
        }

        return _calendars.TryGetValue(serviceId, out var calendar)
            && date >= calendar.StartDate
            && date <= calendar.EndDate
            && calendar.OperatingDays.Contains(date.DayOfWeek);
    }

    internal static DateTimeOffset ToInstant(DateOnly serviceDate, TimeSpan gtfsTime, TimeZoneInfo zone)
    {
        var local = serviceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).Add(gtfsTime);
        if (zone.IsInvalidTime(local))
        {
            throw new ArgumentException($"GTFS time {gtfsTime} falls in a daylight-saving gap on {local:yyyy-MM-dd} in {zone.Id}.");
        }

        // GTFS does not encode which occurrence is intended during a repeated local clock hour.
        // Select the earlier instant deterministically; this ambiguity is rare and will be surfaced in diagnostics later.
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}

