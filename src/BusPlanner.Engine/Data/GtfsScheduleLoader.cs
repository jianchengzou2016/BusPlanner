using System.Globalization;
using System.IO.Compression;
using System.Text;
using BusPlanner.Engine.Models;

namespace BusPlanner.Engine.Data;

public sealed class GtfsScheduleLoader
{
    public GtfsSchedule Load(Stream zipStream)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

        var agencyTable = ReadTable(archive, "agency.txt", required: true)!;
        var timezoneIds = agencyTable.EnumerateRows()
            .Select(x => x.GetRequired("agency_timezone"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (timezoneIds.Length != 1)
        {
            throw new GtfsLoadException("This BusPlanner schedule loader requires all GTFS agencies in a feed to use one timezone.");
        }

        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneIds[0]);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new GtfsLoadException($"GTFS agency_timezone '{timezoneIds[0]}' is not available on this system: {ex.Message}");
        }

        var routes = ReadTable(archive, "routes.txt", required: true)!.EnumerateRows()
            .Select(row => new TransitRoute(
                row.GetRequired("route_id"),
                row.GetOptional("route_short_name"),
                row.GetOptional("route_long_name"),
                row.GetOptionalInt("route_type"),
                row.GetOptional("agency_id")))
            .ToDictionary(x => x.Id, StringComparer.Ordinal);

        var stops = ReadTable(archive, "stops.txt", required: true)!.EnumerateRows()
            .Select(row => new TransitStop(
                row.GetRequired("stop_id"),
                row.GetOptional("stop_name") ?? row.GetRequired("stop_id"),
                row.GetOptional("parent_station"),
                row.GetOptional("stop_code")))
            .ToDictionary(x => x.Id, StringComparer.Ordinal);

        var trips = ReadTable(archive, "trips.txt", required: true)!.EnumerateRows()
            .Select(row => new TransitTrip(
                row.GetRequired("trip_id"),
                row.GetRequired("route_id"),
                row.GetRequired("service_id"),
                row.GetOptional("trip_headsign"),
                row.GetOptionalInt("direction_id")))
            .ToDictionary(x => x.Id, StringComparer.Ordinal);

        foreach (var trip in trips.Values)
        {
            if (!routes.ContainsKey(trip.RouteId))
            {
                throw new GtfsLoadException($"Trip '{trip.Id}' references unknown route '{trip.RouteId}'.");
            }
        }

        var stopTimes = ReadTable(archive, "stop_times.txt", required: true)!.EnumerateRows()
            .Select(row => new TransitStopTime(
                row.GetRequired("trip_id"),
                row.GetRequired("stop_id"),
                row.GetRequiredInt("stop_sequence"),
                row.GetOptionalGtfsTime("arrival_time"),
                row.GetOptionalGtfsTime("departure_time"),
                row.GetOptionalFlag("timepoint", defaultValue: true)))
            .ToArray();

        foreach (var stopTime in stopTimes)
        {
            if (!trips.ContainsKey(stopTime.TripId))
            {
                throw new GtfsLoadException($"Stop time references unknown trip '{stopTime.TripId}'.");
            }

            if (!stops.ContainsKey(stopTime.StopId))
            {
                throw new GtfsLoadException($"Trip '{stopTime.TripId}' references unknown stop '{stopTime.StopId}'.");
            }
        }

        ValidateStopTimeSequences(stopTimes);

        var calendarTable = ReadTable(archive, "calendar.txt", required: false);
        var calendars = calendarTable?.EnumerateRows().Select(ParseCalendar).ToArray() ?? Array.Empty<ServiceCalendar>();
        var exceptions = ReadTable(archive, "calendar_dates.txt", required: false)?.EnumerateRows()
            .Select(row =>
            {
                var type = row.GetRequiredInt("exception_type");
                if (type is not (1 or 2))
                {
                    throw row.Error("exception_type must be 1 (added) or 2 (removed)");
                }

                return new ServiceException(row.GetRequired("service_id"), row.GetRequiredDate("date"), type == 1);
            }).ToArray() ?? Array.Empty<ServiceException>();

        if (calendars.Length == 0 && exceptions.Length == 0)
        {
            throw new GtfsLoadException("The feed must contain calendar.txt, calendar_dates.txt, or both.");
        }

        var frequencies = ReadTable(archive, "frequencies.txt", required: false)?.EnumerateRows()
            .Select(row =>
            {
                var start = row.GetOptionalGtfsTime("start_time") ?? throw row.Error("column 'start_time' is required");
                var end = row.GetOptionalGtfsTime("end_time") ?? throw row.Error("column 'end_time' is required");
                var headway = row.GetRequiredInt("headway_secs");
                if (headway <= 0 || end <= start)
                {
                    throw row.Error("frequency end_time must follow start_time and headway_secs must be positive");
                }

                var exactText = row.GetOptional("exact_times");
                var exactTimes = exactText switch
                {
                    null or "0" => false,
                    "1" => true,
                    _ => throw row.Error("exact_times must be 0 or 1")
                };
                var tripId = row.GetRequired("trip_id");
                if (!trips.ContainsKey(tripId))
                {
                    throw row.Error($"frequency references unknown trip '{tripId}'");
                }

                return new TransitFrequency(tripId, start, end, headway, exactTimes);
            }).ToArray() ?? Array.Empty<TransitFrequency>();

        return new GtfsSchedule(timezone, stops, routes, trips, stopTimes, calendars, exceptions, frequencies);
    }

    public GtfsSchedule LoadFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    private static ServiceCalendar ParseCalendar(CsvRow row)
    {
        var days = new List<DayOfWeek>();
        AddDay("monday", DayOfWeek.Monday);
        AddDay("tuesday", DayOfWeek.Tuesday);
        AddDay("wednesday", DayOfWeek.Wednesday);
        AddDay("thursday", DayOfWeek.Thursday);
        AddDay("friday", DayOfWeek.Friday);
        AddDay("saturday", DayOfWeek.Saturday);
        AddDay("sunday", DayOfWeek.Sunday);
        return new ServiceCalendar(row.GetRequired("service_id"), row.GetRequiredDate("start_date"), row.GetRequiredDate("end_date"), days.ToHashSet());

        void AddDay(string name, DayOfWeek day)
        {
            if (row.GetOptionalFlag(name))
            {
                days.Add(day);
            }
        }
    }

    private static CsvTable? ReadTable(ZipArchive archive, string fileName, bool required)
    {
        var entry = archive.Entries.FirstOrDefault(x =>
            string.Equals(Path.GetFileName(x.FullName), fileName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            if (required)
            {
                throw new GtfsLoadException($"GTFS archive is missing required entry '{fileName}'.");
            }

            return null;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return CsvTable.Read(entry.FullName, reader);
    }

    private static void ValidateStopTimeSequences(IEnumerable<TransitStopTime> stopTimes)
    {
        foreach (var tripGroup in stopTimes.GroupBy(x => x.TripId, StringComparer.Ordinal))
        {
            var sequences = tripGroup.Select(x => x.StopSequence).Order().ToArray();
            if (sequences.Distinct().Count() != sequences.Length)
            {
                throw new GtfsLoadException($"Trip '{tripGroup.Key}' has duplicate stop_sequence values.");
            }
        }
    }
}
