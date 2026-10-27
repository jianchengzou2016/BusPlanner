namespace BusPlanner.Engine.Models;

/// <summary>A stop in the static transit schedule.</summary>
/// <param name="Id">The feed's stop identifier.</param>
/// <param name="Name">The passenger-facing stop name.</param>
/// <param name="ParentStationId">The parent station identifier, when this is a platform or child stop.</param>
/// <param name="StopCode">The passenger-facing stop code, when supplied.</param>
public sealed record TransitStop(string Id, string Name, string? ParentStationId, string? StopCode = null);

/// <summary>A public transit route described by the schedule feed.</summary>
/// <param name="Id">The feed's route identifier.</param>
/// <param name="ShortName">The passenger-facing route label, such as a route number.</param>
/// <param name="LongName">The longer route description, when supplied.</param>
/// <param name="RouteType">The GTFS route type code, when supplied.</param>
/// <param name="AgencyId">The identifier of the operating agency, when supplied.</param>
public sealed record TransitRoute(
    string Id,
    string? ShortName,
    string? LongName,
    int? RouteType,
    string? AgencyId);

/// <summary>A scheduled vehicle journey operating on a route.</summary>
/// <param name="Id">The feed's trip identifier.</param>
/// <param name="RouteId">The route this trip operates on.</param>
/// <param name="ServiceId">The service calendar that determines the operating dates.</param>
/// <param name="HeadSign">The passenger-facing destination or direction text, when supplied.</param>
/// <param name="DirectionId">The feed's direction identifier, when supplied.</param>
public sealed record TransitTrip(
    string Id,
    string RouteId,
    string ServiceId,
    string? HeadSign,
    int? DirectionId);

/// <summary>A scheduled arrival or departure at one stop on a trip.</summary>
/// <param name="TripId">The trip this stop time belongs to.</param>
/// <param name="StopId">The stop served by the trip at this point.</param>
/// <param name="StopSequence">The stop's order within the trip.</param>
/// <param name="ArrivalTime">The GTFS service-day arrival time; values may exceed 24 hours.</param>
/// <param name="DepartureTime">The GTFS service-day departure time; values may exceed 24 hours.</param>
/// <param name="Timepoint">Whether the feed identifies this time as a timepoint.</param>
public sealed record TransitStopTime(
    string TripId,
    string StopId,
    int StopSequence,
    TimeSpan? ArrivalTime,
    TimeSpan? DepartureTime,
    bool Timepoint);

/// <summary>Weekly operating dates for a transit service within a date range.</summary>
/// <param name="ServiceId">The service identifier referenced by trips.</param>
/// <param name="StartDate">The first date covered by this calendar.</param>
/// <param name="EndDate">The last date covered by this calendar.</param>
/// <param name="OperatingDays">The days of the week on which the service operates.</param>
public sealed record ServiceCalendar(
    string ServiceId,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlySet<DayOfWeek> OperatingDays);

/// <summary>A date-specific exception that adds or removes a service day.</summary>
/// <param name="ServiceId">The affected service identifier.</param>
/// <param name="Date">The date on which the exception applies.</param>
/// <param name="Added"><see langword="true"/> when service is added; otherwise service is removed.</param>
public sealed record ServiceException(string ServiceId, DateOnly Date, bool Added);

/// <summary>A GTFS frequency window describing repeated departures for a trip template.</summary>
/// <param name="TripId">The trip template to which the frequency window applies.</param>
/// <param name="StartTime">The beginning of the service-day frequency window.</param>
/// <param name="EndTime">The end of the frequency window.</param>
/// <param name="HeadwaySeconds">The time between departures, in seconds.</param>
/// <param name="ExactTimes">Whether the feed promises departures at the generated times.</param>
public sealed record TransitFrequency(
    string TripId,
    TimeSpan StartTime,
    TimeSpan EndTime,
    int HeadwaySeconds,
    bool ExactTimes);

/// <summary>An indexed snapshot of the static GTFS schedule used to plan journeys.</summary>
public sealed class GtfsSchedule
{
    private readonly Dictionary<string, TransitStopTime[]> _stopTimesByTrip;
    private readonly Dictionary<string, TransitStopTime[]> _stopTimesByStop;
    private readonly Dictionary<string, TransitTrip> _tripsById;
    private readonly Dictionary<string, ServiceCalendar> _calendars;
    private readonly Dictionary<(string ServiceId, DateOnly Date), bool> _exceptions;
    private readonly Dictionary<string, TransitFrequency[]> _frequenciesByTrip;
    private readonly Dictionary<string, string[]> _stopIdsByCode;
    private readonly Dictionary<string, string[]> _stopIdsByNormalizedIdentifier;

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
        _stopIdsByCode = stops.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.StopCode))
            .GroupBy(x => x.StopCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Id).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase);
        _stopIdsByNormalizedIdentifier = stops.Values
            .SelectMany(stop => new[]
            {
                (Value: NormalizeStopCode(stop.Id), StopId: stop.Id),
                (Value: stop.StopCode is null ? null : NormalizeStopCode(stop.StopCode), StopId: stop.Id)
            })
            .Where(x => x.Value is not null)
            .GroupBy(x => x.Value!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(y => y.StopId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase);
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

    /// <summary>Gets the time zone used to interpret service-day times.</summary>
    public TimeZoneInfo AgencyTimeZone { get; }

    /// <summary>Gets stops keyed by their feed stop identifiers.</summary>
    public IReadOnlyDictionary<string, TransitStop> Stops { get; }

    /// <summary>Gets routes keyed by their feed route identifiers.</summary>
    public IReadOnlyDictionary<string, TransitRoute> Routes { get; }

    /// <summary>Gets the number of trips loaded from the feed.</summary>
    public int TripCount => _tripsById.Count;

    /// <summary>Resolves a GTFS stop identifier or public stop code to the internal stop identifier.</summary>
    /// <param name="idOrCode">A stop_id or stop_code, including any leading zeroes.</param>
    /// <returns>The internal stop_id.</returns>
    /// <exception cref="KeyNotFoundException">No stop matches the supplied identifier or code.</exception>
    /// <exception cref="InvalidOperationException">The supplied stop code matches more than one stop.</exception>
    public string ResolveStopId(string idOrCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrCode);
        var value = idOrCode.Trim();
        if (Stops.ContainsKey(value))
        {
            return value;
        }

        if (_stopIdsByCode.TryGetValue(value, out var codeMatches))
        {
            return ResolveUnique(value, codeMatches, "Public stop code");
        }

        var normalized = NormalizeStopCode(value);
        return _stopIdsByNormalizedIdentifier.TryGetValue(normalized, out var normalizedMatches)
            ? ResolveUnique(value, normalizedMatches, "Stop identifier after leading-zero normalization")
            : throw new KeyNotFoundException($"Stop '{value}' was not found in the schedule.");
    }

    private static string ResolveUnique(string value, string[] matches, string matchType) => matches.Length switch
    {
        1 => matches[0],
        _ => throw new InvalidOperationException($"{matchType} '{value}' matches multiple stops.")
    };

    private static string NormalizeStopCode(string value)
    {
        var normalized = value.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    internal IReadOnlyList<TransitStopTime> GetStopTimesForTrip(string tripId) =>
        _stopTimesByTrip.TryGetValue(tripId, out var values) ? values : Array.Empty<TransitStopTime>();

    internal IReadOnlyList<TransitStopTime> GetDeparturesAtStop(string stopId) =>
        _stopTimesByStop.TryGetValue(stopId, out var values) ? values : Array.Empty<TransitStopTime>();

    internal bool TryGetTrip(string tripId, out TransitTrip trip) => _tripsById.TryGetValue(tripId, out trip!);

    internal IReadOnlyList<TransitFrequency> GetFrequencies(string tripId) =>
        _frequenciesByTrip.TryGetValue(tripId, out var values) ? values : Array.Empty<TransitFrequency>();

    /// <summary>Determines whether a service operates on a date after applying calendar exceptions.</summary>
    /// <param name="serviceId">The service identifier to check.</param>
    /// <param name="date">The local service date to check.</param>
    /// <returns><see langword="true"/> when the service operates on the date.</returns>
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
