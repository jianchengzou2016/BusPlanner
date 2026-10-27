# BusPlanner

BusPlanner is a mobile-friendly fixed-journey bus connection planner. The design and milestone decisions are in [PROJECT_BRIEF.md](PROJECT_BRIEF.md).

## Current work

Milestone 1 starts with a reusable .NET journey engine in `src/BusPlanner.Engine`:

- Loads static GTFS ZIP feeds, including stops, routes, trips, stop times, service calendars, calendar exceptions, and exact-time frequency trips.
- Indexes stop times by boarding stop and trip so configured journey legs can be looked up without scanning the full feed on every calculation.
- Parses GTFS-Realtime Protocol Buffer feeds into trip updates and service alerts.
- Calculates up to two catchable first-leg trips and up to two onward alternatives after each transfer buffer, including scheduled/live provenance and no-connection states.
- Excludes canceled trips and trips that skip a configured boarding or alighting stop; warns on detours and modified service; treats relevant no-service and moved-stop alerts as unavailable.
- Uses the agency timezone and GTFS service-day times, including times beyond midnight.

The engine is intentionally independent of the web UI, Azure hosting, and journey-specific stop IDs. A web API, cache/fetch layer, and mobile page are later milestone work.

## Toolchain

The solution targets .NET 10. The machine used to scaffold this project currently has only the .NET 8 SDK; install the .NET 10 SDK before building the solution.

## Feed considerations

- Keep the static GTFS ZIP and any captured real-time feed samples out of source control.
- GTFS-Realtime must be interpreted with the corresponding static GTFS feed. The parser accepts full-dataset feeds and rejects differential feeds.
- Exact-time frequency trips are expanded. Non-exact headway templates are deliberately omitted rather than shown as precise timetable departures; handling them requires an explicit headway estimate policy.
- Service-alert stop selectors are matched against the configured stops. A moved stop remains unavailable unless the journey configuration is updated to a replacement stop.
- The parser uses the official GTFS-Realtime Protocol Buffer schema from [google/transit](https://github.com/google/transit/blob/master/gtfs-realtime/proto/gtfs-realtime.proto), with the schema's Apache 2.0 notice retained in the source file.

