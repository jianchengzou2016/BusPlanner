using BusPlanner.Engine.Models;
using BusPlanner.Engine.Realtime.Generated;

namespace BusPlanner.Engine.Realtime;

public sealed class GtfsRealtimeParser
{
    public RealtimeSnapshot Parse(Stream protobufStream)
    {
        ArgumentNullException.ThrowIfNull(protobufStream);
        var feed = FeedMessage.Parser.ParseFrom(protobufStream);
        if (feed.Header.Incrementality != FeedHeader.Types.Incrementality.FullDataset)
        {
            throw new InvalidDataException("Differential GTFS-Realtime feeds are not supported; a full dataset is required.");
        }

        var timestamp = feed.Header.HasTimestamp
            ? TryFromUnixSeconds(feed.Header.Timestamp)
            : null;
        var tripUpdates = new List<RealtimeTripUpdate>();
        var alerts = new List<RealtimeServiceAlert>();

        foreach (var entity in feed.Entity)
        {
            if (entity.TripUpdate is not null)
            {
                tripUpdates.Add(ParseTripUpdate(entity.TripUpdate));
            }

            if (entity.Alert is not null)
            {
                alerts.Add(ParseAlert(entity.Alert));
            }
        }

        return new RealtimeSnapshot(timestamp, tripUpdates, alerts);
    }

    private static RealtimeTripUpdate ParseTripUpdate(TripUpdate update)
    {
        var trip = update.Trip;
        DateOnly? startDate = DateOnly.TryParseExact(trip.StartDate, "yyyyMMdd", out var date) ? date : null;
        var status = (int)trip.ScheduleRelationship switch
        {
            0 => RealtimeTripStatus.Scheduled,
            3 => RealtimeTripStatus.Canceled,
            7 => RealtimeTripStatus.Deleted,
            _ => RealtimeTripStatus.Other
        };

        var stopUpdates = update.StopTimeUpdate.Select(stop => new RealtimeStopUpdate(
            stop.HasStopSequence ? stop.StopSequence : null,
            stop.HasStopId ? stop.StopId : null,
            stop.Arrival is not null ? ParseEvent(stop.Arrival) : null,
            stop.Departure is not null ? ParseEvent(stop.Departure) : null,
            (int)stop.ScheduleRelationship switch
            {
                0 => RealtimeStopStatus.Scheduled,
                1 => RealtimeStopStatus.Skipped,
                2 => RealtimeStopStatus.NoData,
                _ => RealtimeStopStatus.Other
            })).ToArray();

        return new RealtimeTripUpdate(
            trip.TripId,
            startDate,
            trip.HasStartTime ? trip.StartTime : null,
            status,
            update.HasTimestamp ? TryFromUnixSeconds(update.Timestamp) : null,
            update.HasDelay ? update.Delay : null,
            stopUpdates);
    }

    private static RealtimeStopEvent ParseEvent(TripUpdate.Types.StopTimeEvent value) => new(
        value.HasTime ? TryFromUnixSeconds(value.Time) : null,
        value.HasDelay ? value.Delay : null);

    private static RealtimeServiceAlert ParseAlert(Alert alert)
    {
        var sourcePeriods = alert.ImpactPeriod.Count > 0 ? alert.ImpactPeriod : GetLegacyActivePeriods(alert);
        var periods = sourcePeriods.Select(x => new RealtimeActivePeriod(
            x.HasStart ? TryFromUnixSeconds(x.Start) : null,
            x.HasEnd ? TryFromUnixSeconds(x.End) : null)).ToArray();
        var header = alert.HeaderText?.Translation.FirstOrDefault()?.Text;
        var selectors = alert.InformedEntity.Select(x => new RealtimeAlertSelector(
            x.HasRouteId ? x.RouteId : null,
            x.Trip is not null && x.Trip.HasTripId ? x.Trip.TripId : null,
            x.HasStopId ? x.StopId : null)).ToArray();

        return new RealtimeServiceAlert(
            (int)alert.Effect switch
            {
                1 => RealtimeAlertEffect.NoService,
                4 => RealtimeAlertEffect.Detour,
                6 => RealtimeAlertEffect.ModifiedService,
                9 => RealtimeAlertEffect.StopMoved,
                _ => RealtimeAlertEffect.Other
            },
            header,
            periods,
            selectors);
    }

    private static DateTimeOffset? TryFromUnixSeconds(long seconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static DateTimeOffset? TryFromUnixSeconds(ulong seconds)
    {
        return seconds <= long.MaxValue ? TryFromUnixSeconds((long)seconds) : null;
    }

    private static Google.Protobuf.Collections.RepeatedField<TimeRange> GetLegacyActivePeriods(Alert alert)
    {
#pragma warning disable CS0612, CS0618 // active_period is deprecated in the spec but remains the fallback for older feeds.
        return alert.ActivePeriod;
#pragma warning restore CS0618
    }
}

