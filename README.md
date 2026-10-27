# BusPlanner

BusPlanner is a mobile-friendly fixed-journey bus connection planner. The design and milestone decisions are in [PROJECT_BRIEF.md](PROJECT_BRIEF.md).

## Current work

Milestones 0–2 are complete. The .NET 10 isolated Function API and responsive static page are implemented in `src/BusPlanner.Api` and `src/BusPlanner.Web`.

The engine in `src/BusPlanner.Engine`:

- Loads static GTFS ZIP feeds, including stops, routes, trips, stop times, service calendars, calendar exceptions, and exact-time frequency trips.
- Indexes stop times by boarding stop and trip so configured journey legs can be looked up without scanning the full feed on every calculation.
- Parses GTFS-Realtime Protocol Buffer feeds into trip updates and service alerts.
- Calculates up to two catchable first-leg trips and up to two onward alternatives after each transfer buffer, including scheduled/live provenance and no-connection states.
- Excludes canceled trips and trips that skip a configured boarding or alighting stop; warns on detours and modified service; treats relevant no-service and moved-stop alerts as unavailable.
- Uses the agency timezone and GTFS service-day times, including times beyond midnight.

The current Release build succeeds with no warnings or errors, and all 12 Release tests pass. A local Functions-host smoke check used Translink's SEQ schedule and a fresh TripUpdates feed: the origin stage returned two live first-leg options and clearly reported no onward service to the exact configured destination; the transfer stage also reported no service. The static page was checked at 320, 390, and 768 pixel widths with no horizontal overflow. Feed samples and local journey settings are excluded from this repository.

Local Function settings are created from `src/BusPlanner.Api/local.settings.example.json` and are ignored by Git. They contain the current journey label and stop/route configuration. Public stop codes are resolved through `stop_code` or unique leading-zero normalization of the feed stop ID. The current schedule does not show an onward trip from the configured transfer boarding stop to the exact destination, so the page displays that unavailable connection clearly. It does not silently substitute a nearby stop.

## Toolchain

The solution targets .NET 10. It was verified with .NET SDK 10.0.403. Build and test it with `dotnet build BusPlanner.sln -c Release` and `dotnet test BusPlanner.sln`.

## Feed considerations

- Keep the static GTFS ZIP and any captured real-time feed samples out of source control.
- GTFS-Realtime must be interpreted with the corresponding static GTFS feed. The parser accepts full-dataset feeds and rejects differential feeds.
- Exact-time frequency trips are expanded. Non-exact headway templates are deliberately omitted rather than shown as precise timetable departures; handling them requires an explicit headway estimate policy.
- Service-alert stop selectors are matched against the configured stops. A moved stop remains unavailable unless the journey configuration is updated to a replacement stop.
- The parser uses the official GTFS-Realtime Protocol Buffer schema from [google/transit](https://github.com/google/transit/blob/master/gtfs-realtime/proto/gtfs-realtime.proto), with the schema's Apache 2.0 notice retained in the source file.
- Realtime data is refreshed on demand and cached in-process for 60 seconds. A first-request realtime outage falls back to the timetable; an older cached feed is retained so the planner can mark it stale.
