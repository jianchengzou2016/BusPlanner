using BusPlanner.Engine.Models;

namespace BusPlanner.Engine;

/// <summary>Finds the next two feasible trips at each stage of a configured journey.</summary>
public sealed class JourneyPlanner(
    GtfsSchedule schedule,
    TimeSpan? maximumRealtimeAge = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? realtimeRouteIdPrefixes = null)
{
    private readonly TimeSpan _maximumRealtimeAge = maximumRealtimeAge ?? TimeSpan.FromMinutes(3);
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _realtimeRouteIdPrefixes =
        realtimeRouteIdPrefixes ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    public JourneyPlanResult Plan(JourneyPlanRequest request, RealtimeSnapshot? realtime = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var freshness = realtime?.GetFreshness(request.Now, _maximumRealtimeAge) ?? FeedFreshness.Unavailable;
        var usableRealtime = freshness == FeedFreshness.Fresh ? realtime : null;
        var notices = new List<string>();
        if (freshness == FeedFreshness.Stale)
        {
            notices.Add("The real-time feed is stale; estimates use the timetable where available.");
        }
        else if (freshness is FeedFreshness.MissingTimestamp or FeedFreshness.Unavailable)
        {
            notices.Add("Fresh real-time data is unavailable; estimates use the timetable where available.");
        }

        var readyAt = request.Now + request.RemainingApproachTime;
        var firstOptions = FindCandidates(
                request.Legs[0], 0, readyAt, request.Now, usableRealtime, notices)
            .Take(2)
            .Select(candidate => Expand(candidate, 0, null, request, usableRealtime, notices))
            .ToArray();

        if (firstOptions.Length == 0)
        {
            notices.Add("No eligible first-leg service is catchable after the remaining approach time.");
        }
        else if (firstOptions.Length == 1)
        {
            notices.Add("Only one eligible first-leg service is currently catchable.");
        }

        return new JourneyPlanResult(
            request.Now,
            freshness,
            realtime?.FeedTimestamp,
            firstOptions,
            notices.Distinct(StringComparer.Ordinal).ToArray());
    }

    private JourneyBranch Expand(
        Candidate candidate,
        int legIndex,
        DateTimeOffset? transferReadyAt,
        JourneyPlanRequest request,
        RealtimeSnapshot? realtime,
        List<string> notices)
    {
        var trip = ToEstimate(candidate, legIndex, transferReadyAt);
        if (legIndex == request.Legs.Count - 1)
        {
            return new JourneyBranch(trip, Array.Empty<JourneyBranch>(), IsComplete: true, StatusMessage: null);
        }

        var nextReadyAt = candidate.EstimatedArrival + request.TransferAllowances[legIndex];
        var nextOptions = FindCandidates(
                request.Legs[legIndex + 1], legIndex + 1, nextReadyAt, request.Now, realtime, notices)
            .Take(2)
            .Select(next => Expand(next, legIndex + 1, nextReadyAt, request, realtime, notices))
            .ToArray();

        if (nextOptions.Length == 0)
        {
            var boardName = schedule.Stops[request.Legs[legIndex + 1].BoardStopId].Name;
            return new JourneyBranch(
                trip,
                nextOptions,
                IsComplete: false,
                StatusMessage: $"No feasible onward service from {boardName} after the transfer allowance.");
        }

        return new JourneyBranch(trip, nextOptions, IsComplete: nextOptions.Any(x => x.IsComplete), StatusMessage: null);
    }

    private IEnumerable<Candidate> FindCandidates(
        JourneyLeg leg,
        int legIndex,
        DateTimeOffset notBefore,
        DateTimeOffset calculationTime,
        RealtimeSnapshot? realtime,
        List<string> notices)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(notBefore, schedule.AgencyTimeZone).DateTime);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<Candidate>();

        foreach (var serviceDate in new[] { localDate.AddDays(-1), localDate, localDate.AddDays(1), localDate.AddDays(2) })
        {
            foreach (var boardStopTime in schedule.GetDeparturesAtStop(leg.BoardStopId))
            {
                if (!schedule.TryGetTrip(boardStopTime.TripId, out var trip)
                    || !schedule.IsServiceActive(trip.ServiceId, serviceDate)
                    || !schedule.Routes.TryGetValue(trip.RouteId, out var route)
                    || !RouteMatches(route, leg)
                    || (leg.DirectionId is not null && trip.DirectionId != leg.DirectionId))
                {
                    continue;
                }

                var tripStopTimes = schedule.GetStopTimesForTrip(trip.Id);
                var boardIndex = Array.FindIndex(tripStopTimes.ToArray(), x => x.StopSequence == boardStopTime.StopSequence);
                if (boardIndex < 0)
                {
                    continue;
                }

                var alight = tripStopTimes.Skip(boardIndex + 1).FirstOrDefault(x => x.StopId == leg.AlightStopId);
                if (alight is null)
                {
                    continue;
                }

                var boardDeparture = boardStopTime.DepartureTime ?? boardStopTime.ArrivalTime;
                var alightArrival = alight.ArrivalTime ?? alight.DepartureTime;
                if (boardDeparture is null || alightArrival is null)
                {
                    continue;
                }

                var frequencies = schedule.GetFrequencies(trip.Id);
                if (frequencies.Count > 0 && frequencies.Any(x => !x.ExactTimes))
                {
                    // Non-exact headway service does not promise a departure at an exact timetable time.
                    // Do not present its template time as a scheduled departure.
                    notices.Add($"Frequency-based trip {trip.Id} has no exact departure time and was omitted.");
                }

                var tripTemplateStart = tripStopTimes[0].DepartureTime ?? tripStopTimes[0].ArrivalTime ?? boardDeparture.Value;
                foreach (var frequencyStart in GetExactFrequencyStarts(frequencies))
                {
                    var candidateBoardTime = ShiftByFrequency(boardDeparture.Value, frequencyStart, tripTemplateStart);
                    var candidateAlightTime = ShiftByFrequency(alightArrival.Value, frequencyStart, tripTemplateStart);
                    var key = $"{trip.Id}|{serviceDate:yyyyMMdd}|{frequencyStart?.Ticks.ToString() ?? "scheduled"}|{boardStopTime.StopSequence}";
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    DateTimeOffset scheduledDeparture;
                    DateTimeOffset scheduledArrival;
                    try
                    {
                        scheduledDeparture = GtfsSchedule.ToInstant(serviceDate, candidateBoardTime, schedule.AgencyTimeZone);
                        scheduledArrival = GtfsSchedule.ToInstant(serviceDate, candidateAlightTime, schedule.AgencyTimeZone);
                    }
                    catch (ArgumentException ex)
                    {
                        notices.Add($"Trip {trip.Id} on {serviceDate:yyyy-MM-dd} was omitted: {ex.Message}");
                        continue;
                    }

                    var update = FindTripUpdate(
                        realtime,
                        trip.Id,
                        route.ShortName,
                        serviceDate,
                        frequencyStart,
                        tripTemplateStart,
                        scheduledDeparture,
                        calculationTime);
                    if (update is not null && !IsFresh(update.Timestamp, realtime!.FeedTimestamp, calculationTime))
                    {
                        update = null;
                    }

                    if (update is not null && update.Status is RealtimeTripStatus.Canceled or RealtimeTripStatus.Deleted)
                    {
                        continue;
                    }

                    if (update is not null && update.Status is not RealtimeTripStatus.Scheduled)
                    {
                        notices.Add($"Trip {trip.Id} has an unsupported real-time trip status and was omitted.");
                        continue;
                    }

                    if (IsSkipped(update, tripStopTimes, boardStopTime) || IsSkipped(update, tripStopTimes, alight))
                    {
                        continue;
                    }

                    var departurePrediction = GetPrediction(
                        update, tripStopTimes, boardStopTime, serviceDate, candidateBoardTime,
                        preferDeparture: true, frequencyStart, tripTemplateStart);
                    var arrivalPrediction = GetPrediction(
                        update, tripStopTimes, alight, serviceDate, candidateAlightTime,
                        preferDeparture: false, frequencyStart, tripTemplateStart);

                    var estimatedDeparture = departurePrediction.Time ?? scheduledDeparture;
                    var estimatedArrival = arrivalPrediction.Time ?? scheduledArrival;
                    if (estimatedArrival < estimatedDeparture || estimatedDeparture < notBefore)
                    {
                        continue;
                    }

                    var warnings = GetWarnings(realtime, trip, route, leg, estimatedDeparture, estimatedArrival);
                    if (warnings.Blocking)
                    {
                        continue;
                    }

                    candidates.Add(new Candidate(
                        trip,
                        route,
                        serviceDate,
                        boardStopTime,
                        alight,
                        scheduledDeparture,
                        estimatedDeparture,
                        departurePrediction.Source,
                        scheduledArrival,
                        estimatedArrival,
                        arrivalPrediction.Source,
                        warnings.Messages));
                }
            }
        }

        return candidates
            .OrderBy(x => x.EstimatedDeparture)
            .ThenBy(x => x.Trip.Id, StringComparer.Ordinal)
            .ThenBy(x => x.ServiceDate);
    }

    private static IEnumerable<TimeSpan?> GetExactFrequencyStarts(IReadOnlyList<TransitFrequency> frequencies)
    {
        if (frequencies.Count == 0)
        {
            yield return null;
            yield break;
        }

        foreach (var frequency in frequencies.Where(x => x.ExactTimes))
        {
            var first = frequency.StartTime;
            while (first < frequency.EndTime)
            {
                yield return first;
                first += TimeSpan.FromSeconds(frequency.HeadwaySeconds);
            }
        }
    }

    private static TimeSpan ShiftByFrequency(TimeSpan templateTime, TimeSpan? start, TimeSpan templateStart) =>
        start is null ? templateTime : start.Value + (templateTime - templateStart);

    private RealtimeTripUpdate? FindTripUpdate(
        RealtimeSnapshot? snapshot,
        string tripId,
        string? routeShortName,
        DateOnly serviceDate,
        TimeSpan? frequencyStart,
        TimeSpan tripStartTime,
        DateTimeOffset scheduledDeparture,
        DateTimeOffset calculationTime)
    {
        if (snapshot is null)
        {
            return null;
        }

        var matches = snapshot.TripUpdates.Where(x => x.TripId == tripId).ToArray();
        if (matches.Length > 0)
        {
            var dateMatch = matches.Where(x => x.StartDate == serviceDate).ToArray();
            if (frequencyStart is not null)
            {
                var startText = FormatGtfsTime(frequencyStart.Value);
                var startMatch = dateMatch.FirstOrDefault(x => x.StartTime == startText);
                if (startMatch is not null)
                {
                    return startMatch;
                }
            }

            if (dateMatch.Length == 1)
            {
                return dateMatch[0];
            }

            var undated = matches.Where(x => x.StartDate is null).ToArray();
            if (undated.Length == 1
                && matches.Length == 1
                && scheduledDeparture >= calculationTime - TimeSpan.FromHours(24)
                && scheduledDeparture <= calculationTime + TimeSpan.FromHours(3))
            {
                return undated[0];
            }
        }

        if (string.IsNullOrWhiteSpace(routeShortName))
        {
            return null;
        }

        var prefixes = _realtimeRouteIdPrefixes.TryGetValue(routeShortName, out var configuredPrefixes)
            ? configuredPrefixes
            : new[] { $"{routeShortName}-" };
        var expectedStartTime = FormatGtfsTime(frequencyStart ?? tripStartTime);
        var routeMatches = snapshot.TripUpdates.Where(update =>
            update.StartDate == serviceDate
            && update.StartTime == expectedStartTime
            && update.RouteId is not null
            && prefixes.Any(prefix => update.RouteId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return routeMatches.Length == 1 ? routeMatches[0] : null;
    }

    private bool IsSkipped(
        RealtimeTripUpdate? update,
        IReadOnlyList<TransitStopTime> tripStopTimes,
        TransitStopTime target)
    {
        var stopUpdate = FindStopUpdate(update, tripStopTimes, target);
        return stopUpdate?.Status == RealtimeStopStatus.Skipped;
    }

    private Prediction GetPrediction(
        RealtimeTripUpdate? update,
        IReadOnlyList<TransitStopTime> tripStopTimes,
        TransitStopTime target,
        DateOnly serviceDate,
        TimeSpan targetGtfsTime,
        bool preferDeparture,
        TimeSpan? frequencyStart,
        TimeSpan templateStart)
    {
        var targetSchedule = GtfsSchedule.ToInstant(serviceDate, targetGtfsTime, schedule.AgencyTimeZone);
        if (update is null)
        {
            return new Prediction(null, EstimateSource.Scheduled);
        }

        var propagatedDelay = update.TripDelaySeconds;
        foreach (var stopTime in tripStopTimes.OrderBy(x => x.StopSequence))
        {
            var stopUpdate = FindStopUpdate(update, tripStopTimes, stopTime);
            if (stopUpdate?.Status == RealtimeStopStatus.NoData)
            {
                propagatedDelay = null;
            }

            var isTarget = stopTime.StopSequence == target.StopSequence;
            if (isTarget)
            {
                if (stopUpdate?.Status == RealtimeStopStatus.NoData)
                {
                    return new Prediction(null, EstimateSource.Scheduled);
                }

                var eventValue = preferredEvent(stopUpdate, preferDeparture);
                if (eventValue?.Time is not null)
                {
                    return new Prediction(eventValue.Time, EstimateSource.Realtime);
                }

                if (eventValue?.DelaySeconds is not null)
                {
                    return new Prediction(targetSchedule + TimeSpan.FromSeconds(eventValue.DelaySeconds.Value), EstimateSource.Realtime);
                }

                if (propagatedDelay is not null)
                {
                    return new Prediction(targetSchedule + TimeSpan.FromSeconds(propagatedDelay.Value), EstimateSource.Realtime);
                }

                return new Prediction(null, EstimateSource.Scheduled);
            }

            if (stopUpdate is null || stopUpdate.Status == RealtimeStopStatus.NoData)
            {
                continue;
            }

            var priorEvent = stopUpdate.Departure ?? stopUpdate.Arrival;
            if (priorEvent?.DelaySeconds is not null)
            {
                propagatedDelay = priorEvent.DelaySeconds.Value;
            }
            else if (priorEvent?.Time is not null)
            {
                var scheduledTime = GetScheduledEvent(stopTime, serviceDate, preferDeparture, frequencyStart, templateStart);
                if (scheduledTime is not null)
                {
                    propagatedDelay = (int)Math.Round((priorEvent.Time.Value - scheduledTime.Value).TotalSeconds);
                }
            }
        }

        return new Prediction(null, EstimateSource.Scheduled);

        static RealtimeStopEvent? preferredEvent(RealtimeStopUpdate? value, bool departure) =>
            departure ? value?.Departure ?? value?.Arrival : value?.Arrival ?? value?.Departure;
    }

    private DateTimeOffset? GetScheduledEvent(
        TransitStopTime stopTime,
        DateOnly serviceDate,
        bool departure,
        TimeSpan? frequencyStart,
        TimeSpan templateStart)
    {
        var value = departure
            ? stopTime.DepartureTime ?? stopTime.ArrivalTime
            : stopTime.ArrivalTime ?? stopTime.DepartureTime;
        return value is null
            ? null
            : GtfsSchedule.ToInstant(serviceDate, ShiftByFrequency(value.Value, frequencyStart, templateStart), schedule.AgencyTimeZone);
    }

    private static RealtimeStopUpdate? FindStopUpdate(
        RealtimeTripUpdate? update,
        IReadOnlyList<TransitStopTime> tripStopTimes,
        TransitStopTime target)
    {
        if (update is null)
        {
            return null;
        }

        var sequence = target.StopSequence < 0 ? null : (uint?)target.StopSequence;
        var bySequence = sequence is null
            ? null
            : update.StopUpdates.FirstOrDefault(x => x.StopSequence == sequence);
        if (bySequence is not null)
        {
            return bySequence;
        }

        var stopMatches = tripStopTimes.Count(x => SameStopIdentifier(x.StopId, target.StopId));
        return stopMatches == 1
            ? update.StopUpdates.FirstOrDefault(x => x.StopId is not null && SameStopIdentifier(x.StopId, target.StopId))
            : null;
    }

    private (bool Blocking, IReadOnlyList<string> Messages) GetWarnings(
        RealtimeSnapshot? snapshot,
        TransitTrip trip,
        TransitRoute route,
        JourneyLeg leg,
        DateTimeOffset departure,
        DateTimeOffset arrival)
    {
        if (snapshot is null)
        {
            return (false, Array.Empty<string>());
        }

        var warnings = new List<string>();
        var blocking = false;
        foreach (var alert in snapshot.Alerts)
        {
            if (!IsActiveDuring(alert, departure, arrival))
            {
                continue;
            }

            var matching = alert.Selectors.Where(selector =>
                (selector.RouteId is null || IsRouteSelectorMatch(selector.RouteId, trip.RouteId, route.ShortName))
                && (selector.TripId is null || selector.TripId == trip.Id)
                &&
                (selector.StopId is null || SameStopIdentifier(selector.StopId, leg.BoardStopId) || SameStopIdentifier(selector.StopId, leg.AlightStopId)))
                .ToArray();
            if (matching.Length == 0)
            {
                continue;
            }

            if (alert.Effect == RealtimeAlertEffect.NoService
                || (alert.Effect == RealtimeAlertEffect.StopMoved
                    && matching.Any(x => x.StopId is not null
                        && (SameStopIdentifier(x.StopId, leg.BoardStopId) || SameStopIdentifier(x.StopId, leg.AlightStopId)))))
            {
                blocking = true;
                continue;
            }

            var warning = alert.Header ?? (alert.Effect switch
            {
                RealtimeAlertEffect.StopMoved => "A configured stop has moved.",
                RealtimeAlertEffect.Detour => "A detour affects this service.",
                RealtimeAlertEffect.ModifiedService => "Modified service affects this trip.",
                _ => "A service alert affects this trip."
            });
            warnings.Add(warning);
        }

        return (blocking, warnings.Distinct(StringComparer.Ordinal).ToArray());

        bool IsRouteSelectorMatch(string selectorRouteId, string scheduleRouteId, string? routeShortName)
        {
            if (selectorRouteId == scheduleRouteId)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(routeShortName))
            {
                return false;
            }

            var prefixes = _realtimeRouteIdPrefixes.TryGetValue(routeShortName, out var configuredPrefixes)
                ? configuredPrefixes
                : new[] { $"{routeShortName}-" };
            return prefixes.Any(prefix => selectorRouteId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool IsActiveDuring(RealtimeServiceAlert alert, DateTimeOffset start, DateTimeOffset end)
    {
        if (alert.ActivePeriods.Count == 0)
        {
            return true;
        }

        return alert.ActivePeriods.Any(period =>
            (period.Start is null || period.Start <= end)
            && (period.End is null || period.End >= start));
    }

    private static bool SameStopIdentifier(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
        || string.Equals(NormalizeStopIdentifier(left), NormalizeStopIdentifier(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeStopIdentifier(string value)
    {
        var normalized = value.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private TripEstimate ToEstimate(Candidate candidate, int legIndex, DateTimeOffset? transferReadyAt)
    {
        var routeName = candidate.Route.ShortName ?? candidate.Route.LongName ?? candidate.Route.Id;
        var board = schedule.Stops[candidate.BoardTime.StopId];
        var alight = schedule.Stops[candidate.AlightTime.StopId];
        return new TripEstimate(
            legIndex,
            candidate.Trip.Id,
            candidate.Route.Id,
            routeName,
            candidate.Trip.HeadSign,
            board.Id,
            board.Name,
            alight.Id,
            alight.Name,
            candidate.ServiceDate,
            candidate.ScheduledDeparture,
            candidate.EstimatedDeparture,
            candidate.DepartureSource,
            candidate.ScheduledArrival,
            candidate.EstimatedArrival,
            candidate.ArrivalSource,
            transferReadyAt,
            candidate.Warnings);
    }

    private static bool RouteMatches(TransitRoute route, JourneyLeg leg) =>
        leg.AllowedRouteShortNames.Contains(route.ShortName ?? string.Empty);

    private bool IsFresh(DateTimeOffset? updateTimestamp, DateTimeOffset? feedTimestamp, DateTimeOffset now)
    {
        var timestamp = updateTimestamp ?? feedTimestamp;
        return timestamp is not null && now >= timestamp.Value && now - timestamp.Value <= _maximumRealtimeAge;
    }

    private static string FormatGtfsTime(TimeSpan value)
    {
        var hours = (long)value.TotalHours;
        return $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    private void ValidateRequest(JourneyPlanRequest request)
    {
        if (request.Legs.Count == 0)
        {
            throw new ArgumentException("A journey must contain at least one leg.", nameof(request));
        }

        if (request.RemainingApproachTime < TimeSpan.Zero || request.RemainingApproachTime > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Remaining approach time must be between zero and 24 hours.");
        }

        if (request.TransferAllowances.Count != request.Legs.Count - 1)
        {
            throw new ArgumentException("Provide one transfer allowance for each change between journey legs.", nameof(request));
        }

        if (request.TransferAllowances.Any(x => x < TimeSpan.Zero || x > TimeSpan.FromHours(2)))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Each transfer allowance must be between zero and two hours.");
        }

        for (var i = 0; i < request.Legs.Count; i++)
        {
            var leg = request.Legs[i];
            if (string.IsNullOrWhiteSpace(leg.BoardStopId) || !schedule.Stops.ContainsKey(leg.BoardStopId))
            {
                throw new ArgumentException($"Journey leg {i + 1} has an unknown boarding stop.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(leg.AlightStopId) || !schedule.Stops.ContainsKey(leg.AlightStopId))
            {
                throw new ArgumentException($"Journey leg {i + 1} has an unknown alighting stop.", nameof(request));
            }

            if (leg.BoardStopId == leg.AlightStopId)
            {
                throw new ArgumentException($"Journey leg {i + 1} must board and alight at different stops.", nameof(request));
            }

            if (leg.AllowedRouteShortNames.Count == 0)
            {
                throw new ArgumentException($"Journey leg {i + 1} must allow at least one route short name.", nameof(request));
            }
        }
    }

    private sealed record Prediction(DateTimeOffset? Time, EstimateSource Source);

    private sealed record Candidate(
        TransitTrip Trip,
        TransitRoute Route,
        DateOnly ServiceDate,
        TransitStopTime BoardTime,
        TransitStopTime AlightTime,
        DateTimeOffset ScheduledDeparture,
        DateTimeOffset EstimatedDeparture,
        EstimateSource DepartureSource,
        DateTimeOffset ScheduledArrival,
        DateTimeOffset EstimatedArrival,
        EstimateSource ArrivalSource,
        IReadOnlyList<string> Warnings);
}
