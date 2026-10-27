# BusPlanner — Design Requirements and Milestones

**Status:** Initial product brief  
**Date:** 26 October 2026

## 1. Product goal

BusPlanner is a mobile-friendly public website that helps a passenger follow a known, multi-bus journey and make a practical connection when services are delayed. It should answer:

> If I take this bus, when should I reach the interchange, which connecting bus can I catch after allowing time to change platforms, and when should I arrive at my destination?

The first end-to-end example is a configured afternoon journey with one bus to an interchange, a platform transfer, then an eligible onward bus to the destination. Exact stops, routes, and service eligibility remain configurable and will be confirmed later; they are not assumptions baked into the initial design.

## 2. Product principles

- Works in a current mobile browser on iPhone, iPad, and Android; no app-store installation or account is required.
- Shows the next two eligible first-leg departures, and calculates a feasible onward connection for each candidate.
- Accounts for how long the passenger needs to reach the first boarding stop. A bus departing before the passenger can get there is not a catchable option and should not be presented as one.
- Lets the passenger adjust the quick link's default starting point within the configured journey and the remaining time before they can board there; these are request/session choices and need not be saved to a user profile.
- Uses a fresh Translink real-time prediction when available. If unavailable, it may show the timetable estimate, clearly labelled as scheduled, along with feed freshness.
- Is real-time-first for configured school/work/home journeys: live bus position and stop-level predictions inform the current service options; the static timetable is a clearly labelled fallback rather than the primary status.
- Shows when the information was last updated. Missing, stale, or ambiguous source data must not be presented as a live prediction.
- Treats a journey URL as a complete, shareable calculation request. A convenience quick link can resolve to the same journey parameters without asking the passenger to edit them.
- Has no sign-in and stores no personal user information. Journey details in a shareable URL may be visible to anyone who has that URL, so public links should use generic labels.
- Keeps operational safeguards proportionate to a public service: validate journey parameters, cache shared timetable/realtime data, and avoid uncontrolled upstream polling or request volume.
- Does not require user accounts or personal-data security features, but treats the website and API as internet-accessible and protects against accidental or abusive request volume that could create hosting or bandwidth charges.

## 3. Initial journey behavior

For each configured journey, the link supplies a default starting stage and remaining approach time. The user can change both on the page for the current request:

1. Let the passenger choose among the journey's configured starting points, such as the original boarding stop or a transfer platform. When they choose a later stage, skip earlier legs and calculate only the remaining journey.
2. Apply the configured default or user-adjusted remaining approach time to the selected starting stop/platform. Find the next two eligible trips departing no earlier than that time, subject to configured route and direction constraints. If useful, show when the passenger should leave to catch each option.
3. Estimate each candidate trip's arrival at the next interchange using stop-level real-time data when fresh and available; otherwise use its scheduled arrival and label it accordingly.
4. Add the configured transfer allowance to the interchange arrival time.
5. Find the two earliest eligible onward trips, ordered by their predicted departure from the configured transfer platform, that depart at or after the transfer-ready time. Present the first as the primary connection and the next as the backup. Apply available live departure predictions and cancellation/skipped-stop information when deciding whether each service remains catchable. When live predictions are unavailable, use scheduled departure order and label it as timetable-based.
6. Estimate arrival at the destination stop, with the same live-versus-scheduled labelling.
7. For each of the two first-leg candidates, present the primary and backup onward connections in a compact mobile view, including the selected starting point, remaining approach time, route, stop/platform, departure/arrival times, transfer allowance, source status, and last update time.

Use a configurable transfer allowance for each specific arrival-platform to onward-boarding-platform pair, with four minutes as an initial value for a short change such as the Mater Hill example. A longer platform walk, such as changing to a distant platform at a large station, may need a longer default. Show the allowance within the transfer summary and let the passenger adjust it for the current request through a compact inline edit, rather than a separate always-visible settings control. Keep that override local to the current request; do not save it to a user profile. If no feasible onward bus is found, say so clearly rather than implying a connection exists.

The connection engine should not assume the primary onward bus will operate or remain catchable during busy periods. The backup is the next distinct feasible onward trip after the primary, recalculated from the same first-leg arrival and transfer allowance; if realtime cancellation or skipped-stop data makes a candidate unusable, exclude it and continue to the next eligible service. If no second feasible onward trip exists, show that no backup is currently available. Fresh live data is the primary evidence for current conditions. Prefer provider stop-level arrival/departure predictions where available; use matched vehicle-position data to inform a stop estimate only when the selected trip and route progress can be identified reliably. A vehicle position by itself must not be presented as a precise arrival time.

The approach time is separate from the transfer allowance. It represents the remaining time until the passenger can board at the selected starting stop/platform, including walking or getting ready to leave. Illustrative quick-link defaults may range from under 10 minutes for a nearby starting point, 10–15 minutes for a longer walk, and 15–20 minutes for an after-work journey; configure these per journey and confirm actual values later. A user can adjust a 10-minute default to 5 minutes when already partway to the stop. The user can also choose a specific later boarding platform in the journey as the current starting point; earlier journey legs are then omitted, and approach time defaults to zero at that selected platform. If the passenger is still walking between platforms or otherwise cannot board immediately, they can override that remaining approach time. Transfer allowance is applied only between legs when the engine calculates a connection; it is not added again when starting at a later platform. This is a manual, per-request override and is not saved server-side.

The initial release does not request or share mobile-device GPS. Browser location provides coordinates, not a walking-time estimate; deriving an arrival time at a stop would also require a mapping/routing source, location permission, and additional privacy and failure handling. GPS-based assistance can be considered later if manual stage and time controls prove inadequate.

## 4. Technical direction

- **Language/runtime:** C# on .NET 10 LTS.
- **Application:** ASP.NET Core, initially one deployable web app serving the mobile-first interface and journey API.
- **Data:** Translink SEQ GTFS-Realtime is the primary source for live trip/stop updates and, if needed, vehicle positions. Static GTFS supplies route/stop structure and the clearly labelled scheduled fallback.
- **Persistence:** No user database for the first version. Cache shared transport data in a deployment-appropriate way; journey parameters come from the request URL or a small, static quick-link mapping in app configuration.
- **Hosting:** Azure is the preferred target, subject to a verified deployment path and cost estimate for the selected .NET hosting model. Staying within available free allowances/credits is a goal, not a cost guarantee.
- **Architecture:** Keep timetable parsing, real-time feed handling, connection selection, and web presentation separate enough to reason about and replace independently. Prefer indexed timetable lookups and shared feed caching over scanning and downloading the full feed for each visitor request.

### Journey URL and parameter boundary

The canonical request should contain the passenger-facing inputs needed to calculate a journey, without exposing operational details as user parameters. The initial parameter set is:

- Starting boarding stop and destination stop.
- Ordered journey legs, each with its boarding/alighting stop and allowed route numbers (and direction only when needed to disambiguate).
- Approach time to the first boarding stop.
- Transfer allowance for each configured arrival-platform to onward-boarding-platform pair, with a journey-level default where appropriate.
- Optional assessment time; if omitted, use the current time in the configured local timezone.

Feed URLs, cache durations, provider freshness thresholds, API request limits, and Azure settings remain server-side configuration. Use concise parameter names and sensible defaults so a journey link does not become a long list of implementation knobs.

A full URL can carry the complete journey request, allowing a user to bookmark or share a particular configuration without an account. A short, memorable quick link may instead use a journey key mapped to the same parameter set in static app configuration. That mapping is convenience, not authentication or privacy: anyone with the link may view the journey, and URL parameters or configured labels can reveal stop/location details.

### Live feed caching and cost controls

- Cache the shared Translink realtime feed on demand. A journey request first checks the cache; only a missing or expired cache entry triggers an upstream fetch.
- Start with a configurable one-minute freshness interval. Do not run a background poller in the first version solely to refresh the cache.
- Coalesce simultaneous cache misses so a burst of incoming requests causes at most one upstream fetch per feed/cache interval. The cache may hold TripUpdates and, only if the calculation needs it, VehiclePositions; do not request vehicle GPS data just because a page refreshes.
- If the upstream feed is unavailable, return a clearly labelled timetable estimate or stale-data state according to the journey rules; do not repeatedly retry the provider for every concurrent request.
- Apply bounded request rates and validation to the public API, and set a minimum browser refresh interval. Prefer allowing only supported journey parameters/routes rather than arbitrary expensive feed queries.
- Use Azure budget and anomaly alerts, and document any available automated action to disable or limit the API at a cost threshold. Alerts alone are not a hard spending cap; incoming requests, app execution, and outbound bandwidth may still be billed even when upstream feed calls are cached.

The public site needs no login for the family use case, but a public URL is still reachable by others. A short link is not access control. Cache and rate limits reduce exposure to request spikes; Azure bills usage to the subscription/resource owner, subject to that hosting service's pricing, free grants, and subscription terms.

## 5. Scope boundaries

### In scope

- A public, responsive browser experience.
- Configurable multi-leg journeys and generated or configured quick links.
- A simple selector for configured journey stages/boarding stops and an editable remaining approach-time value.
- A compact inline way to adjust the transfer allowance for the current request.
- Two candidate first-leg services, each with a primary and backup onward connection when available.
- Clear scheduled/live labels and data freshness.
- One configured journey working end to end before generalizing to additional journeys.
- Azure deployment planning and cost controls.
- On-demand shared realtime-feed caching with request-volume safeguards appropriate for a public endpoint.

### Out of scope for the initial release

- Any changes to AnkoDisplay, its Bluetooth/display application, or its configuration.
- AnkoDisplay’s `dashboard/transport_poc/`: it is an early prototype and is explicitly **not** part of BusPlanner’s implementation scope. It may not be imported, modified, or treated as a dependency.
- User accounts, authentication, saved personal profiles, or a server-side user database.
- General-purpose door-to-door itinerary search, walking navigation, or automatic discovery of arbitrary routes.
- Mobile GPS collection or GPS-derived walking-time estimates.
- QLDTraffic road incident analysis, traffic-based delay inference, and unrelated weather/display features.
- A native mobile app or mandatory progressive-web-app installation.

AnkoDisplay may remain available as a read-only reference for general project ideas. BusPlanner’s implementation and files belong only in the BusPlanner workspace.

## 6. Milestones

### Milestone 0 — Confirm product and data contract

- Freeze the initial journey inputs: stop IDs, route constraints, direction, transfer platform, transfer allowance, and destination.
- Set a configurable first-stop approach time for each journey; keep it distinct from the interchange transfer allowance.
- Record the behavior when only scheduled data is available, when a feed is stale, and when a connection cannot be made.
- Confirm current Translink feed access, attribution requirements, and refresh expectations.
- Select and cost-check an Azure hosting model that supports the chosen .NET application.
- Estimate cost exposure from incoming API calls, compute, storage, and outbound bandwidth; identify a practical threshold response in addition to budget alerts.

**Exit condition:** One journey can be represented as configuration without embedding stop/route assumptions in the calculation code; deployment and data-source constraints are documented.

### Milestone 1 — Build the C# journey engine

- Create the .NET solution in BusPlanner.
- Implement static GTFS loading/indexing, service-day and timezone handling, route/stop trip matching, and candidate departure lookup.
- Implement GTFS-Realtime parsing, trip/stop matching, freshness assessment, and scheduled fallback representation.
- Implement the two-first-leg candidate calculation and the primary plus backup onward-service selection after each transfer allowance, accounting for available realtime cancellation/skipped-stop data.

**Exit condition:** The engine returns a structured result for both candidate services, including live/scheduled provenance and a clear no-connection or unavailable-data state.

### Milestone 2 — Deliver one mobile journey page

- Build the responsive journey page and API response for the first configured journey.
- Show the next two first-leg options with their onward services and final arrival estimates.
- Make source freshness, scheduled fallbacks, and no-connection states understandable at a glance.
- Keep configuration out of public source when it contains personal labels or private deployment settings.

**Exit condition:** The journey can be followed on a phone-sized browser from first departure through destination, with times and source status represented honestly.

### Milestone 3 — Add journey quick links

- Accept the canonical journey parameters from a URL and support multiple journey definitions without user accounts or a user-profile database.
- Provide stable short links mapped through static app configuration as a convenience over full parameter URLs.
- Use generic display labels and avoid publishing a directory of family routes.

**Exit condition:** Each intended journey opens directly from its link and the link’s privacy implications are understood.

### Milestone 4 — Deploy and operate on Azure

- Deploy the selected .NET app and public web experience over HTTPS.
- Configure on-demand shared timetable/realtime caching, concurrent-miss coalescing, bounded request controls, and useful service diagnostics without logging personal journey data unnecessarily.
- Set Azure budget/anomaly alerts and document deployment, rollback, feed outages, cost assumptions, and what action can reduce or stop API charges if usage spikes.

**Exit condition:** The app is reachable on mobile browsers and has a documented operating/cost plan. Actual cost depends on hosting configuration, usage, and available subscription benefits.

## 7. Open decisions

- Confirm the physical boarding, transfer, and destination stops and acceptable route variants when ready; stop details can be resolved after the initial structure is in place.
- Confirm default transfer allowances for the configured platform pairs; use four minutes only as a starting value for short platform changes.
- Show each transfer allowance in the itinerary and allow a per-request inline adjustment without adding a permanent settings panel.
- Confirm each journey's approach time to its first stop and whether the user wants a “leave by” time shown.
- Confirm the configured start-stage choices and which platform should be selected when a passenger is already at an interchange.
- Use specific boarding stops/platforms as start-stage choices; default remaining approach time to zero at the selected platform and apply transfer allowance only between journey legs.
- Decide the refresh interval and maximum acceptable feed age based on provider data and app usage.
- Decide the concise public URL syntax and whether the first release needs both full parameter URLs and short configured quick links; both avoid user accounts, and neither hides journey details from link recipients.
- Verify the exact Azure hosting option, .NET support, quotas, and likely cost before deployment.

## 8. Acceptance summary for the first release

The first release is complete when one configured journey opens from a quick link on a mobile browser, lets the passenger change the starting stage and remaining approach time, excludes trips that depart before the passenger can reach the selected starting stop/platform, shows the next two catchable first-leg options, and for each calculates the primary and next feasible backup onward service in predicted departure order after the transfer allowance. Fresh live bus data drives the result, with scheduled estimates clearly labelled as fallbacks. It estimates final arrival and clearly distinguishes fresh real-time predictions from timetable-only estimates or unavailable data. Repeated page refreshes during the cache interval must reuse the shared feed result, and concurrent cache misses must not fan out into duplicate provider requests. The app must run without sign-in and without storing user profiles, while having documented request and cost safeguards for its public endpoint.

