# BusPlanner

BusPlanner is a mobile-friendly fixed-journey bus connection planner. The design and milestone decisions are in [PROJECT_BRIEF.md](PROJECT_BRIEF.md).

## Current work

Milestones 0–3 are implemented. The .NET 10 isolated Function API and responsive static page are in `src/BusPlanner.Api` and `src/BusPlanner.Web`. Azure Static Web Apps deployment is in progress.

The engine in `src/BusPlanner.Engine`:

- Loads static GTFS ZIP feeds, including stops, routes, trips, stop times, service calendars, calendar exceptions, and exact-time frequency trips.
- Indexes stop times by boarding stop and trip so configured journey legs can be looked up without scanning the full feed on every calculation.
- Parses GTFS-Realtime Protocol Buffer feeds into trip updates and service alerts.
- Supports configured branches (alternative journey paths), each made of ordered legs. A leg can allow multiple route short names, which is useful when different services use the same stops and direction at different times (for example, routes 150 and 153).
- Calculates the next two catchable first-leg services across the configured alternatives and up to two onward alternatives after each transfer buffer, including scheduled/live provenance and no-connection states.
- Excludes canceled trips and trips that skip a configured boarding or alighting stop; warns on detours and modified service; treats relevant no-service and moved-stop alerts as unavailable.
- Uses the agency timezone and GTFS service-day times, including times beyond midnight.

The Release build succeeds with no warnings or errors, and all 13 engine tests pass. The schedule loader streams GTFS records and retains only configured routes to reduce download parsing work. API responses include server timing for schedule wait/cache load/download/parse, realtime load, planning, and total server time; the page also reports client request and render timing. The schedule is loaded once per Functions instance; realtime feeds are refreshed on demand and cached for 60 seconds. Feed samples and local journey settings are excluded from source control.

`local.settings.json` remains necessary for Azure Functions Core Tools, which reads host values such as `AzureWebJobsStorage` and `FUNCTIONS_WORKER_RUNTIME`. Copy it from `src/BusPlanner.Api/local.settings.example.json`; keep journey configuration out of it. Journey profiles and BusPlanner runtime settings live in structured `src/BusPlanner.Api/appsettings.Development.json`, which is ignored by Git and should contain actual stop codes and labels. Copy the tracked `appsettings.example.json` as a starting point. Journey configuration is grouped by named profile, then branch (an alternative path), then ordered legs. Each leg has its own boarding and alighting stop and can allow one or more route short names. Put routes that serve the same leg/stops as alternatives in the same `AllowedRouteShortNames` list; use a separate branch when the routes follow different paths or require different transfers. For example, the sample BSHS profile has a direct route 150 branch and an M1 branch whose onward leg accepts either route 150 or 153.

Transfer starting points are derived from later legs' boarding stops, so duplicate interchange configuration is not needed. The API accepts a profile's stable `QuickLink`, configuration key, or label through the `journey` query parameter and supports `stage`, `approachMinutes`, and `transferMinutes` overrides. Public stop codes are resolved through `stop_code` or unique leading-zero normalization of the feed stop ID. Departure searches stop at the end of the current local calendar day and do not offer tomorrow's first service. If the configured services have ended for the day, the planner reports no remaining service.

## Quick links and adding a profile

The standard shared link is the site root followed by the profile slug in the `journey` query parameter:

```text
https://<your-site>/?journey=churchie-home
https://<your-site>/?journey=bshs-home
```

The `?` begins the URL query string; `journey` selects the configured profile. Quick links are public convenience URLs, not private or access-controlled links. Use generic labels if you do not want a person's name shown on the page.

To add another journey, add a profile object under `BusPlanner.Journeys` in `src/BusPlanner.Api/appsettings.Development.json`:

1. Set `Label` to the title displayed in the page and `QuickLink` to a unique, memorable slug using lowercase letters, digits, and hyphens (for example, `jessica-home`).
2. Set `OriginStopCode`, `DestinationStopCode`, and the approach/transfer defaults.
3. Add each alternative path to the `Branches` array. Within each branch, add ordered `Legs` with `BoardStopCode`, `AlightStopCode`, and one or more `AllowedRouteShortNames` values.
4. Use the same leg's allowed-route list for services that share that leg's stops and direction. Add a separate branch when a route follows a different path or needs different transfers.
5. Open `/?journey=<QuickLink>` to use and share the new profile. Restart the local Functions host after changing settings.

To show a return-trip button beside a profile title, set that profile's `ReturnJourneyQuickLink` to the other profile's `QuickLink`. Configure the return profile separately with its own stops, route directions, branches, and legs. The button appears only when the referenced quick link resolves to a configured profile. For example, the BSHS → Home profile can point to `home-bshs`, and the Home → BSHS profile uses `"QuickLink": "home-bshs"`.

The profile key is an internal configuration name; `QuickLink` is the stable value intended for shared URLs. The API continues to accept profile keys and labels for compatibility. Keep real stop codes and personal labels in ignored `appsettings.Development.json`; the tracked example uses blank stop-code placeholders.

### Azure Static Web Apps journey settings

Azure Static Web Apps limits API application-setting names to 64 characters. It does not provide a file-upload step for `appsettings.Development.json`. Instead, use that file as your source: copy the JSON object under `BusPlanner.Journeys`, then in the Static Web App open **Settings → Environment variables**, select **Production**, add `BusPlanner__Journeys`, and paste that object as its value. The value must start and end with braces and contain profile keys directly; do not include the outer `BusPlanner` object or wrap the entire value in quotation marks. Whitespace and line breaks are fine. For example:

```json
{
  "Churchie2HomeJourney": {
    "Label": "Churchie -> Home",
    "QuickLink": "churchie-home",
    "OriginStopCode": "010145",
    "DestinationStopCode": "010711",
    "DefaultApproachMinutes": 10,
    "DefaultTransferMinutes": 4,
    "Branches": [
      {
        "Legs": [
          { "BoardStopCode": "010145", "AlightStopCode": "019051", "AllowedRouteShortNames": ["235"] },
          { "BoardStopCode": "019063", "AlightStopCode": "010711", "AllowedRouteShortNames": ["150"] }
        ]
      }
    ]
  }
}
```

Include all profiles (Churchie, Work, BSHS, and any return profiles) in this single JSON object. Locally, store it under `BusPlanner.Journeys` in `appsettings.Development.json`; Azure receives the same journey object as the value of `BusPlanner__Journeys`. Add scalar settings such as `BusPlanner__RealtimeCacheSeconds` and `BusPlanner__MaximumRealtimeAgeSeconds` separately if they differ from defaults. `appsettings.Development.json` is ignored by Git; `appsettings.example.json` is the tracked template with blank stop-code placeholders. After saving environment variables, allow the API to restart, then open the Production site and check the journey response.

The web UI supports English and Simplified Chinese (Mandarin text) through the language selector in the header. **System** follows the browser/OS primary language: Chinese selects Chinese, and all other languages select English. A manual English/Chinese selection is remembered in that browser. The URL can override both with `lang=en` or `lang=zh`, for example `/?journey=home-brownsplain&lang=zh`; return-trip links preserve an explicit language choice. Journey titles translate known terms such as Home → 家 and BrownsPlain → 布朗平原; configured labels without a known translation remain as entered. Common configured stops have Chinese display names, with official English names retained where useful for identification.

Page/API timing metrics are hidden during normal use. To show them for diagnostics, add `debug=1` to the URL, for example `/?journey=work-home&debug=1`.

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

To try a configured profile, open `http://localhost:4280/?journey=work-home` (or use the profile key/label configured in `appsettings.Development.json`). The sample Work → Home profile evaluates the next two departures across routes 153 and 138: route 153 is direct, while route 138 uses a walking transfer to route 150. The sample BSHS profile shows alternatives sharing the same leg: routes 150 and 153 are listed under one leg's allowed route names. Replace the blank stop codes in the example with valid local stop codes before using those sample profiles. Journey profiles are in ignored `src/BusPlanner.Api/appsettings.Development.json`; restart `func start` after changing them.

To try the page on a phone or tablet connected to the same Wi-Fi, open the web server's URL using the computer's LAN IP address and port (for example, `http://192.168.1.20:4280`). Windows Firewall may prompt to allow the CLI to accept local network connections.

## Feed considerations

- Keep the static GTFS ZIP and any captured real-time feed samples out of source control.
- GTFS-Realtime must be interpreted with the corresponding static GTFS feed. The parser accepts full-dataset feeds and rejects differential feeds.
- Exact-time frequency trips are expanded. Non-exact headway templates are deliberately omitted rather than shown as precise timetable departures; handling them requires an explicit headway estimate policy.
- Service-alert stop selectors are matched against the configured stops. A moved stop remains unavailable unless the journey configuration is updated to a replacement stop.
- The parser uses the official GTFS-Realtime Protocol Buffer schema from [google/transit](https://github.com/google/transit/blob/master/gtfs-realtime/proto/gtfs-realtime.proto), with the schema's Apache 2.0 notice retained in the source file.
- Realtime data is refreshed on demand and cached in-process for 60 seconds. A first-request realtime outage falls back to the timetable; an older cached feed is retained so the planner can mark it stale.
