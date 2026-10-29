using System.IO.Compression;
using System.Text;
using Google.Protobuf;
using BusPlanner.Engine;
using BusPlanner.Engine.Data;
using BusPlanner.Engine.Models;
using BusPlanner.Engine.Realtime;
using BusPlanner.Engine.Realtime.Generated;
using Xunit;

namespace BusPlanner.Engine.Tests;

public sealed class JourneyPlannerTests
{
    private static readonly DateOnly ServiceDate = new(2026, 10, 27);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 14, 0, 0, TimeSpan.FromHours(10));

    [Fact]
    public void Plan_ReturnsTwoFirstLegsAndTwoFeasibleConnectionsForEach()
    {
        var schedule = LoadSchedule();
        var planner = new JourneyPlanner(schedule);

        var result = planner.Plan(CreateRequest());

        Assert.Equal(FeedFreshness.Unavailable, result.FeedFreshness);
        Assert.Equal(new[] { "235a", "235b" }, result.FirstLegOptions.Select(x => x.Trip.TripId));
        Assert.Equal(new[] { "150b", "150c" }, result.FirstLegOptions[0].NextLegOptions.Select(x => x.Trip.TripId));
        Assert.Equal(new[] { "150d", "150e" }, result.FirstLegOptions[1].NextLegOptions.Select(x => x.Trip.TripId));
        Assert.Equal(Now.AddMinutes(24), result.FirstLegOptions[0].NextLegOptions[0].Trip.EstimatedDeparture);
        Assert.Equal(Now.AddMinutes(29), result.FirstLegOptions[1].NextLegOptions[0].Trip.TransferReadyAt);
    }

    [Fact]
    public void Plan_ExcludesCanceledTripsAndTripsThatSkipTheBoardingStop()
    {
        var schedule = LoadSchedule();
        var planner = new JourneyPlanner(schedule);
        var realtime = new RealtimeSnapshot(
            Now,
            new[]
            {
                new RealtimeTripUpdate("235a", ServiceDate, null, RealtimeTripStatus.Canceled, Now, null, Array.Empty<RealtimeStopUpdate>()),
                new RealtimeTripUpdate("235b", ServiceDate, null, RealtimeTripStatus.Scheduled, Now, null,
                    new[] { new RealtimeStopUpdate(1, "S", null, null, RealtimeStopStatus.Skipped) })
            },
            Array.Empty<RealtimeServiceAlert>());

        var result = planner.Plan(CreateRequest(), realtime);

        Assert.Equal("235c", result.FirstLegOptions[0].Trip.TripId);
        Assert.DoesNotContain(result.FirstLegOptions.Select(x => x.Trip.TripId), x => x == "235b");
    }

    [Fact]
    public void Plan_UsesScheduledTimesWhenTheRealtimeFeedIsStale()
    {
        var schedule = LoadSchedule();
        var planner = new JourneyPlanner(schedule);
        var staleRealtime = new RealtimeSnapshot(
            Now.AddMinutes(-4),
            new[] { new RealtimeTripUpdate("235a", ServiceDate, null, RealtimeTripStatus.Canceled, Now.AddMinutes(-4), null, Array.Empty<RealtimeStopUpdate>()) },
            Array.Empty<RealtimeServiceAlert>());

        var result = planner.Plan(CreateRequest(), staleRealtime);

        Assert.Equal(FeedFreshness.Stale, result.FeedFreshness);
        Assert.Equal("235a", result.FirstLegOptions[0].Trip.TripId);
        Assert.Equal(EstimateSource.Scheduled, result.FirstLegOptions[0].Trip.DepartureSource);
        Assert.Contains("stale", result.Notices[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_SkipsDeparturesThatPrecedeTheRemainingApproachTime()
    {
        var planner = new JourneyPlanner(LoadSchedule());
        var request = CreateRequest() with { RemainingApproachTime = TimeSpan.FromMinutes(6) };

        var result = planner.Plan(request);

        Assert.Equal("235b", result.FirstLegOptions[0].Trip.TripId);
        Assert.All(result.FirstLegOptions, option => Assert.True(option.Trip.EstimatedDeparture >= Now.AddMinutes(6)));
    }

    [Fact]
    public void Plan_UsesRealtimeArrivalToDecideWhetherAnOnwardTripCanBeCaught()
    {
        var planner = new JourneyPlanner(LoadSchedule());
        var realtime = new RealtimeSnapshot(
            Now,
            new[]
            {
                new RealtimeTripUpdate("235a", ServiceDate, null, RealtimeTripStatus.Scheduled, null, null,
                    new[] { new RealtimeStopUpdate(2, "X2", new RealtimeStopEvent(Now.AddMinutes(28), null), null, RealtimeStopStatus.Scheduled) })
            },
            Array.Empty<RealtimeServiceAlert>());

        var result = planner.Plan(CreateRequest(), realtime);
        var firstTrip = result.FirstLegOptions[0];

        Assert.Equal("235a", firstTrip.Trip.TripId);
        Assert.Equal(Now.AddMinutes(28), firstTrip.Trip.EstimatedArrival);
        Assert.Equal(Now.AddMinutes(32), firstTrip.NextLegOptions[0].Trip.TransferReadyAt);
        Assert.Equal("150e", firstTrip.NextLegOptions[0].Trip.TripId);
    }

    [Fact]
    public void Plan_MatchesRealtimeByRoutePrefixAndStartTimeWhenTripIdDiffers()
    {
        var planner = new JourneyPlanner(LoadSchedule());
        var realtime = new RealtimeSnapshot(
            Now,
            new[]
            {
                new RealtimeTripUpdate("different-trip-id", ServiceDate, "14:05:00", RealtimeTripStatus.Scheduled, null, null,
                    new[] { new RealtimeStopUpdate(2, "X2", new RealtimeStopEvent(Now.AddMinutes(22), null), null, RealtimeStopStatus.Scheduled) },
                    "235-another-operator-code")
            },
            Array.Empty<RealtimeServiceAlert>());

        var result = planner.Plan(CreateRequest(), realtime);

        Assert.Equal("235a", result.FirstLegOptions[0].Trip.TripId);
        Assert.Equal(Now.AddMinutes(22), result.FirstLegOptions[0].Trip.EstimatedArrival);
        Assert.Equal(EstimateSource.Realtime, result.FirstLegOptions[0].Trip.ArrivalSource);
    }

    [Fact]
    public void Schedule_ResolvesPublicStopCodesThroughGtfsStopCode()
    {
        var schedule = LoadSchedule();

        Assert.Equal("S", schedule.ResolveStopId("090001"));
        Assert.Equal("S", schedule.ResolveStopId("90001"));
    }

    [Fact]
    public void Plan_ExcludesOnwardTripsThatAreCanceledOrSkipTheBoardingStop()
    {
        var planner = new JourneyPlanner(LoadSchedule());
        var realtime = new RealtimeSnapshot(
            Now,
            new[]
            {
                new RealtimeTripUpdate("150a", ServiceDate, null, RealtimeTripStatus.Canceled, Now, null, Array.Empty<RealtimeStopUpdate>()),
                new RealtimeTripUpdate("150b", ServiceDate, null, RealtimeTripStatus.Scheduled, Now, null,
                    new[] { new RealtimeStopUpdate(1, "X1", null, null, RealtimeStopStatus.Skipped) })
            },
            Array.Empty<RealtimeServiceAlert>());

        var result = planner.Plan(CreateRequest(), realtime);

        Assert.Equal(new[] { "150c", "150d" }, result.FirstLegOptions[0].NextLegOptions.Select(x => x.Trip.TripId));
    }

    [Fact]
    public void Plan_ReportsWhenAServiceAlertMakesTheOnwardLegUnavailable()
    {
        var planner = new JourneyPlanner(LoadSchedule());
        var realtime = new RealtimeSnapshot(
            Now,
            Array.Empty<RealtimeTripUpdate>(),
            new[]
            {
                new RealtimeServiceAlert(
                    RealtimeAlertEffect.NoService,
                    "Route 150 is not operating.",
                    Array.Empty<RealtimeActivePeriod>(),
                    new[] { new RealtimeAlertSelector("R150", null, null) })
            });

        var result = planner.Plan(CreateRequest(), realtime);
        var first = result.FirstLegOptions[0];

        Assert.Empty(first.NextLegOptions);
        Assert.False(first.IsComplete);
        Assert.Contains("No feasible onward service", first.StatusMessage);
    }

    [Fact]
    public void Plan_RecognizesTripsAfterMidnightAsBelongingToThePreviousServiceDate()
    {
        var planner = new JourneyPlanner(LoadSchedule());
        var afterMidnight = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.FromHours(10));
        var request = CreateRequest() with { Now = afterMidnight, RemainingApproachTime = TimeSpan.Zero };

        var result = planner.Plan(request);

        Assert.Equal("235overnight", result.FirstLegOptions[0].Trip.TripId);
        Assert.Equal(ServiceDate, result.FirstLegOptions[0].Trip.ServiceDate);
        Assert.Equal(afterMidnight.AddMinutes(5), result.FirstLegOptions[0].Trip.EstimatedDeparture);
    }

    [Fact]
    public void Plan_DoesNotOfferDeparturesAfterTheCurrentLocalCalendarDayEnds()
    {
        var planner = new JourneyPlanner(LoadSchedule());
        var lateEvening = new DateTimeOffset(2026, 10, 27, 23, 50, 0, TimeSpan.FromHours(10));
        var request = CreateRequest() with { Now = lateEvening, RemainingApproachTime = TimeSpan.Zero };

        var result = planner.Plan(request);

        Assert.Empty(result.FirstLegOptions);
        Assert.Contains(result.Notices, notice => notice.Contains("No eligible first-leg service", StringComparison.Ordinal));
    }

    [Fact]
    public void Schedule_AppliesCalendarDateExceptionsOverTheWeeklyCalendar()
    {
        var schedule = LoadSchedule(includeExceptions: true);

        Assert.False(schedule.IsServiceActive("WK", ServiceDate));
        Assert.True(schedule.IsServiceActive("WK", new DateOnly(2026, 10, 25)));
    }

    [Fact]
    public void RealtimeParser_ParsesTripCancellationAndSkippedStop()
    {
        var feed = new FeedMessage
        {
            Header = new FeedHeader
            {
                GtfsRealtimeVersion = "2.0",
                Timestamp = (ulong)Now.ToUnixTimeSeconds()
            }
        };
        feed.Entity.Add(new FeedEntity
        {
            Id = "canceled-trip",
            TripUpdate = new TripUpdate
            {
                Trip = new TripDescriptor
                {
                    TripId = "235a",
                    RouteId = "235-example",
                    StartDate = "20261027",
                    ScheduleRelationship = TripDescriptor.Types.ScheduleRelationship.Canceled
                }
            }
        });
        feed.Entity.Add(new FeedEntity
        {
            Id = "skipped-stop",
            TripUpdate = new TripUpdate
            {
                Trip = new TripDescriptor { TripId = "235b", StartDate = "20261027" },
                StopTimeUpdate =
                {
                    new TripUpdate.Types.StopTimeUpdate
                    {
                        StopId = "S",
                        StopSequence = 1,
                        ScheduleRelationship = TripUpdate.Types.StopTimeUpdate.Types.ScheduleRelationship.Skipped
                    }
                }
            }
        });
        using var stream = new MemoryStream();
        stream.Write(feed.ToByteArray());
        stream.Position = 0;

        var parsed = new GtfsRealtimeParser().Parse(stream);

        Assert.Equal(RealtimeTripStatus.Canceled, Assert.Single(parsed.TripUpdates, x => x.TripId == "235a").Status);
        Assert.Equal("235-example", Assert.Single(parsed.TripUpdates, x => x.TripId == "235a").RouteId);
        Assert.Equal(RealtimeStopStatus.Skipped, Assert.Single(parsed.TripUpdates.Single(x => x.TripId == "235b").StopUpdates).Status);
        Assert.Equal(Now, parsed.FeedTimestamp);
    }

    private static JourneyPlanRequest CreateRequest() => new(
        new[]
        {
            new JourneyLeg("S", "X2", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "235" }),
            new JourneyLeg("X1", "H", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "150" })
        },
        Now,
        TimeSpan.FromMinutes(5),
        new[] { TimeSpan.FromMinutes(4) });

    private static GtfsSchedule LoadSchedule(bool includeExceptions = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add("agency.txt", "agency_id,agency_name,agency_url,agency_timezone\nA,Transit,https://example.invalid,Australia/Brisbane\n");
            Add("routes.txt", "route_id,agency_id,route_short_name,route_long_name,route_type\nR235,A,235,Route 235,3\nR150,A,150,Route 150,3\n");
            Add("stops.txt", "stop_id,stop_code,stop_name\nS,090001,School\nX2,090063,Interchange platform 2\nX1,090051,Interchange platform 1\nH,090695,Home\n");
            Add("trips.txt", "route_id,service_id,trip_id,trip_headsign,direction_id\nR235,WK,235a,Interchange,0\nR235,WK,235b,Interchange,0\nR235,WK,235c,Interchange,0\nR235,WK,235overnight,Interchange,0\nR150,WK,150a,Home,1\nR150,WK,150b,Home,1\nR150,WK,150c,Home,1\nR150,WK,150d,Home,1\nR150,WK,150e,Home,1\n");
            Add("stop_times.txt", "trip_id,arrival_time,departure_time,stop_id,stop_sequence,timepoint\n" +
                "235a,14:05:00,14:05:00,S,1,1\n235a,14:20:00,14:20:00,X2,2,1\n" +
                "235b,14:10:00,14:10:00,S,1,1\n235b,14:25:00,14:25:00,X2,2,1\n" +
                "235c,14:15:00,14:15:00,S,1,1\n235c,14:30:00,14:30:00,X2,2,1\n" +
                "235overnight,24:05:00,24:05:00,S,1,1\n235overnight,24:20:00,24:20:00,X2,2,1\n" +
                "150a,14:21:00,14:21:00,X1,1,1\n150a,14:50:00,14:50:00,H,2,1\n" +
                "150b,14:24:00,14:24:00,X1,1,1\n150b,14:53:00,14:53:00,H,2,1\n" +
                "150c,14:28:00,14:28:00,X1,1,1\n150c,14:58:00,14:58:00,H,2,1\n" +
                "150d,14:30:00,14:30:00,X1,1,1\n150d,14:59:00,14:59:00,H,2,1\n" +
                "150e,14:35:00,14:35:00,X1,1,1\n150e,15:04:00,15:04:00,H,2,1\n");
            Add("calendar.txt", "service_id,monday,tuesday,wednesday,thursday,friday,saturday,sunday,start_date,end_date\nWK,1,1,1,1,1,0,0,20260101,20261231\n");
            if (includeExceptions)
            {
                Add("calendar_dates.txt", "service_id,date,exception_type\nWK,20261027,2\nWK,20261025,1\n");
            }

            void Add(string name, string contents)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(contents);
            }
        }

        stream.Position = 0;
        return new GtfsScheduleLoader().Load(stream);

    }
}
