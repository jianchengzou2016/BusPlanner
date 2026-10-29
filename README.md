# BusPlanner

BusPlanner is a mobile-friendly fixed-journey bus connection planner. The design and milestone decisions are in [PROJECT_BRIEF.md](PROJECT_BRIEF.md).

## Current work

Milestones 0–3 are complete. The .NET 10 isolated Function API and responsive static page are implemented in `src/BusPlanner.Api` and `src/BusPlanner.Web`. Azure deployment and operations planning are now in discussion.

The engine in `src/BusPlanner.Engine`:

- Loads static GTFS ZIP feeds, including stops, routes, trips, stop times, service calendars, calendar exceptions, and exact-time frequency trips.
- Indexes stop times by boarding stop and trip so configured journey legs can be looked up without scanning the full feed on every calculation.
- Parses GTFS-Realtime Protocol Buffer feeds into trip updates and service alerts.
- Supports configured branches (alternative journey paths), each made of ordered legs. A leg can allow multiple route short names, which is useful when different services use the same stops and direction at different times (for example, routes 150 and 153).
- Calculates the next two catchable first-leg services across the configured alternatives and up to two onward alternatives after each transfer buffer, including scheduled/live provenance and no-connection states.
- Excludes canceled trips and trips that skip a configured boarding or alighting stop; warns on detours and modified service; treats relevant no-service and moved-stop alerts as unavailable.
- Uses the agency timezone and GTFS service-day times, including times beyond midnight.

The Release build succeeds with no warnings or errors, and all 13 engine tests pass. The schedule loader streams GTFS records and retains only configured routes to reduce download parsing work. API responses include server timing for schedule wait/cache load/download/parse, realtime load, planning, and total server time; the page also reports client request and render timing. The schedule is loaded once per Functions instance; realtime feeds are refreshed on demand and cached for 60 seconds. Feed samples and local journey settings are excluded from source control.

Local Function settings are created from `src/BusPlanner.Api/local.settings.example.json`; `local.settings.json` is ignored by Git and should contain the actual stop codes and labels. Journey configuration is grouped by named profile, then branch (an alternative path), then ordered legs. Each leg has its own boarding and alighting stop and can allow one or more route short names. Put routes that serve the same leg/stops as alternatives in the same `AllowedRouteShortNames` list; use a separate branch when the routes follow different paths or require different transfers. For example, the sample BSHS profile has a direct route 150 branch and an M1 branch whose onward leg accepts either route 150 or 153.

Transfer starting points are derived from later legs' boarding stops, so duplicate interchange configuration is not needed. The API accepts a profile's stable `QuickLink`, configuration key, or label through the `journey` query parameter and supports `stage`, `approachMinutes`, and `transferMinutes` overrides. Public stop codes are resolved through `stop_code` or unique leading-zero normalization of the feed stop ID. Departure searches stop at the end of the current local calendar day and do not offer tomorrow's first service. If the configured services have ended for the day, the planner reports no remaining service.

## Quick links and adding a profile

The standard shared link is the site root followed by the profile slug in the `journey` query parameter:

```text
https://<your-site>/?journey=churchie-home
https://<your-site>/?journey=bshs-home
```

The `?` begins the URL query string; `journey` selects the configured profile. Quick links are public convenience URLs, not private or access-controlled links. Use generic labels if you do not want a person's name shown on the page.

To add another journey, add settings under `BusPlanner__Journeys__<ProfileKey>__...` in `src/BusPlanner.Api/local.settings.json`:

1. Set `Label` to the title displayed in the page and `QuickLink` to a unique, memorable slug using lowercase letters, digits, and hyphens (for example, `jessica-home`).
2. Set `OriginStopCode`, `DestinationStopCode`, and the approach/transfer defaults.
3. Add each alternative path under `Branches__<branch index>`. Within each branch, add ordered `Legs__<leg index>` with `BoardStopCode`, `AlightStopCode`, and one or more `AllowedRouteShortNames__<index>` values.
4. Use the same leg's allowed-route list for services that share that leg's stops and direction. Add a separate branch when a route follows a different path or needs different transfers.
5. Open `/?journey=<QuickLink>` to use and share the new profile. Restart the local Functions host after changing settings.

The profile key is an internal configuration name; `QuickLink` is the stable value intended for shared URLs. The API continues to accept profile keys and labels for compatibility. Keep real stop codes and personal labels in ignored `local.settings.json`; the tracked example uses blank stop-code placeholders.

Chinese-language UI support, including translated controls and a language selector, is a later enhancement and is not part of the initial Azure deployment milestone.

## Toolchain

The solution targets .NET 10. It was verified with .NET SDK 10.0.403. Build and test it with `dotnet build BusPlanner.sln -c Release` and `dotnet test BusPlanner.sln`.

## Run locally

To try the web page against the local API, run the API and the static web app in separate PowerShell windows. Run these commands from the BusPlanner repository root.

In the first window, start the Azure Functions API:

```powershell
Set-Location .\src\BusPlanner.Api
func start
```

The API should be available at `http://localhost:7071`. Leave this window open.

In a second window, start the web UI with the Static Web Apps CLI proxying `/api` requests to that API:

```powershell
Set-Location .\
npx --yes @azure/static-web-apps-cli start .\src\BusPlanner.Web --api-devserver-url http://localhost:7071
```

Open the local web URL printed by the CLI (usually `http://localhost:4280`). Keep both processes running while using the page. The UI uses relative `/api/journey` requests, so the CLI proxy is needed for local UI-to-API communication; opening the API URL directly only shows the API response.

To try a configured profile, open `http://localhost:4280/?journey=work-home` (or use the profile key/label configured in local settings). The sample Work → Home profile evaluates the next two departures across routes 153 and 138: route 153 is direct, while route 138 uses a walking transfer to route 150. The sample BSHS profile shows alternatives sharing the same leg: routes 150 and 153 are listed under one leg's allowed route names. Replace the blank stop codes in the example with valid local stop codes before using those sample profiles. The local profile values are in ignored `src/BusPlanner.Api/local.settings.json` and are not published in the repository. Restart `func start` after changing them.

To try the page on a phone or tablet connected to the same Wi-Fi, open the web server's URL using the computer's LAN IP address and port (for example, `http://192.168.1.20:4280`). Windows Firewall may prompt to allow the CLI to accept local network connections.

## Feed considerations

- Keep the static GTFS ZIP and any captured real-time feed samples out of source control.
- GTFS-Realtime must be interpreted with the corresponding static GTFS feed. The parser accepts full-dataset feeds and rejects differential feeds.
- Exact-time frequency trips are expanded. Non-exact headway templates are deliberately omitted rather than shown as precise timetable departures; handling them requires an explicit headway estimate policy.
- Service-alert stop selectors are matched against the configured stops. A moved stop remains unavailable unless the journey configuration is updated to a replacement stop.
- The parser uses the official GTFS-Realtime Protocol Buffer schema from [google/transit](https://github.com/google/transit/blob/master/gtfs-realtime/proto/gtfs-realtime.proto), with the schema's Apache 2.0 notice retained in the source file.
- Realtime data is refreshed on demand and cached in-process for 60 seconds. A first-request realtime outage falls back to the timetable; an older cached feed is retained so the planner can mark it stale.
