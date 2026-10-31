const stageInput = document.querySelector("#stage");
const approachInput = document.querySelector("#approach-minutes");
const transferInput = document.querySelector("#transfer-minutes");
const transferControl = document.querySelector("#transfer-control");
const refreshButton = document.querySelector("#refresh-button");
const refreshNote = document.querySelector("#refresh-note");
const resultElement = document.querySelector("#results");
const noticeElement = document.querySelector("#notices");
const feedStatusElement = document.querySelector("#feed-status");
const connectionState = document.querySelector("#connection-state");
const titleElement = document.querySelector("#journey-title");
const returnJourneyLink = document.querySelector("#return-journey-link");
const subtitleElement = document.querySelector("#journey-subtitle");
const performanceElement = document.querySelector("#performance-status");
const journeyKey = new URLSearchParams(window.location.search).get("journey");
let approachSetAt = Date.now();
let approachBaseMinutes = 10;
let defaultOriginApproachMinutes = 10;
let firstResponse = true;
let firstResultElapsedMs = null;
let refreshInProgress = false;
let journeyHasTransfers = false;

function selectedApproachMinutes() {
  const elapsed = Math.floor((Date.now() - approachSetAt) / 60_000);
  return Math.max(0, approachBaseMinutes - elapsed);
}

function showRemainingApproach() {
  approachInput.value = String(selectedApproachMinutes());
}

function syncControls() {
  const atInterchange = stageInput.value !== "origin";
  stageInput.closest(".control-row").hidden = stageInput.options.length <= 1;
  transferControl.hidden = atInterchange || !journeyHasTransfers;
  approachInput.setAttribute("aria-label", atInterchange ? "Minutes until ready to board at the selected transfer stop" : "Minutes until reaching the first boarding stop");
  refreshNote.textContent = atInterchange
    ? "The time to board counts down while this page is open."
    : "The approach time counts down; results refresh every minute.";
}

function formatTime(value) {
  if (!value) return "—";
  return new Intl.DateTimeFormat("en-AU", {
    timeZone: "Australia/Brisbane",
    hour: "numeric",
    minute: "2-digit"
  }).format(new Date(value));
}

function sourceLabel(source) {
  return source === "realtime" ? "Live prediction" : "Timetable";
}

function makeTimePair(label, time, source) {
  const pair = document.createElement("div");
  pair.className = "time-pair";
  const labelElement = document.createElement("span");
  labelElement.className = "time-label";
  labelElement.textContent = label;
  const valueElement = document.createElement("span");
  valueElement.className = `time-value ${source === "realtime" ? "source-live" : "source-scheduled"}`;
  valueElement.textContent = `${formatTime(time)} · ${sourceLabel(source)}`;
  pair.append(labelElement, valueElement);
  return pair;
}

function renderTrip(branch, optionLabel, depth = 0) {
  const trip = branch.trip;
  const container = document.createElement("article");
  container.className = `branch${depth > 0 ? " branch-level-1" : ""}`;

  const heading = document.createElement("div");
  heading.className = "trip-heading";
  const routeBlock = document.createElement("div");
  const route = document.createElement("div");
  route.className = "trip-route";
  route.textContent = `Route ${trip.routeShortName}`;
  const direction = document.createElement("div");
  direction.className = "trip-direction";
  direction.textContent = trip.headSign || `${trip.boardStopName} → ${trip.alightStopName}`;
  routeBlock.append(route, direction);
  const kind = document.createElement("span");
  kind.className = "trip-kind";
  kind.textContent = optionLabel;
  heading.append(routeBlock, kind);

  const times = document.createElement("div");
  times.className = "trip-time-row";
  times.append(
    makeTimePair(`Depart · ${trip.boardStopName}`, trip.estimatedDeparture, trip.departureSource),
    makeTimePair(`Arrive · ${trip.alightStopName}`, trip.estimatedArrival, trip.arrivalSource)
  );
  container.append(heading, times);

  if (trip.transferReadyAt) {
    const ready = document.createElement("p");
    ready.className = "trip-kind";
    ready.textContent = `Ready to board after transfer: ${formatTime(trip.transferReadyAt)}`;
    container.append(ready);
  }

  if (branch.statusMessage) {
    const status = document.createElement("p");
    status.className = "branch-status";
    status.textContent = branch.statusMessage;
    container.append(status);
  }

  if (trip.warnings?.length) {
    const warning = document.createElement("p");
    warning.className = "trip-warnings";
    warning.textContent = trip.warnings.join(" · ");
    container.append(warning);
  }

  for (const [index, next] of (branch.nextLegOptions || []).entries()) {
    container.append(renderTrip(next, index === 0 ? "Connection 1" : `Connection ${index + 1} · backup`, depth + 1));
  }
  return container;
}

function renderResults(response) {
  const plan = response.plan;
  const selectedStage = stageInput.value;
  stageInput.replaceChildren(new Option("First stop", "origin"));
  for (const stage of response.availableStartingStages || []) {
    stageInput.add(new Option(stage.label, stage.key));
  }
  if ([...stageInput.options].some(option => option.value === selectedStage)) stageInput.value = selectedStage;
  journeyHasTransfers = response.hasTransfers;
  syncControls();
  titleElement.textContent = response.journeyLabel || "BusPlanner";
  if (response.returnJourneyQuickLink && response.returnJourneyLabel) {
    returnJourneyLink.href = `/?journey=${encodeURIComponent(response.returnJourneyQuickLink)}`;
    returnJourneyLink.textContent = response.returnJourneyLabel;
    returnJourneyLink.hidden = false;
  } else {
    returnJourneyLink.hidden = true;
  }
  subtitleElement.textContent = response.startingStage !== "origin"
    ? `Starting from ${response.startingStopName} · ${response.approachMinutes} min until ready to board`
    : `Starting from ${response.startingStopName} · ${response.approachMinutes} min to reach the stop`;

  const freshness = (plan.feedFreshness || "unavailable").toLowerCase();
  feedStatusElement.dataset.freshness = freshness;
  const updated = plan.feedTimestamp ? `Feed updated ${formatTime(plan.feedTimestamp)}` : "No feed timestamp";
  feedStatusElement.textContent = `${freshness === "fresh" ? "Live feed" : freshness === "stale" ? "Stale feed" : "Schedule fallback"} · ${updated}`;

  const notices = [...(plan.notices || [])];
  if (!response.alertsAvailable) notices.push("Service alerts could not be loaded; trip and timetable data are still shown.");
  noticeElement.hidden = notices.length === 0;
  noticeElement.replaceChildren();
  if (notices.length) {
    const list = document.createElement("ul");
    for (const message of notices) {
      const item = document.createElement("li");
      item.textContent = message;
      list.append(item);
    }
    noticeElement.append(list);
  }

  resultElement.replaceChildren();
  if (!plan.firstLegOptions?.length) {
    const empty = document.createElement("div");
    empty.className = "card empty-card";
    empty.textContent = "No catchable service was found for this starting point and remaining time.";
    resultElement.append(empty);
    return;
  }

  plan.firstLegOptions.forEach((branch, index) => {
    const card = document.createElement("section");
    card.className = "card option-card";
    const banner = document.createElement("div");
    banner.className = "option-banner";
    const title = document.createElement("span");
    title.textContent = index === 0 ? "Next bus" : "Following bus · backup";
    const tag = document.createElement("span");
    tag.className = "tag";
    tag.textContent = index === 0 ? "Primary" : "Backup";
    banner.append(title, tag);
    card.append(banner, renderTrip(branch, index === 0 ? "First leg" : "First leg · backup"));
    resultElement.append(card);
  });
}

async function refreshJourney() {
  if (refreshInProgress) return;
  refreshInProgress = true;
  refreshButton.disabled = true;
  connectionState.textContent = "Updating…";
  connectionState.dataset.state = "loading";
  const requestedStage = stageInput.value;
  const isFirstResponse = firstResponse;
  try {
    const params = new URLSearchParams({
      stage: requestedStage,
      transferMinutes: String(Math.max(0, Math.min(120, Number.parseInt(transferInput.value, 10) || 0)))
    });
    if (journeyKey) params.set("journey", journeyKey);
    if (!firstResponse) params.set("approachMinutes", String(selectedApproachMinutes()));
    const apiStartedAt = performance.now();
    const response = await fetch(`/api/journey?${params.toString()}`, { cache: "no-store" });
    const data = await response.json();
    const apiElapsedMs = performance.now() - apiStartedAt;
    if (!response.ok) throw new Error(data.error || `Request failed (${response.status}).`);
    if (stageInput.value !== requestedStage) {
      window.setTimeout(refreshJourney, 0);
      return;
    }
    if (firstResponse) {
      defaultOriginApproachMinutes = data.defaultOriginApproachMinutes;
      transferInput.value = String(data.defaultTransferMinutes);
      approachBaseMinutes = data.approachMinutes;
      approachSetAt = Date.now();
      showRemainingApproach();
      firstResponse = false;
    }
    const renderStartedAt = performance.now();
    renderResults(data);
    const renderElapsedMs = performance.now() - renderStartedAt;
    if (isFirstResponse && firstResultElapsedMs === null) firstResultElapsedMs = performance.now();
    const initialLoadText = firstResultElapsedMs === null
      ? "First result: waiting"
      : `First result: ${(firstResultElapsedMs / 1000).toFixed(2)} s`;
    performanceElement.textContent = `${initialLoadText} · Latest API round trip: ${Math.round(apiElapsedMs)} ms · Render: ${Math.round(renderElapsedMs)} ms`;
    performanceElement.dataset.withinTarget = String(firstResultElapsedMs !== null && firstResultElapsedMs < 2000);
    connectionState.textContent = "Updated just now";
    connectionState.dataset.state = "ready";
  } catch (error) {
    resultElement.replaceChildren();
    const card = document.createElement("div");
    card.className = "card error-card";
    card.textContent = error instanceof Error ? error.message : "Journey data could not be loaded.";
    resultElement.append(card);
    connectionState.textContent = "Could not update";
    connectionState.dataset.state = "error";
  } finally {
    refreshButton.disabled = false;
    refreshInProgress = false;
  }
}

stageInput.addEventListener("change", () => {
  approachBaseMinutes = stageInput.value !== "origin" ? 0 : defaultOriginApproachMinutes;
  approachSetAt = Date.now();
  showRemainingApproach();
  syncControls();
  refreshJourney();
});

approachInput.addEventListener("input", () => {
  approachBaseMinutes = Math.max(0, Math.min(120, Number.parseInt(approachInput.value, 10) || 0));
  approachSetAt = Date.now();
});
approachInput.addEventListener("change", refreshJourney);
transferInput.addEventListener("change", refreshJourney);
refreshButton.addEventListener("click", refreshJourney);

syncControls();
refreshJourney();
window.setInterval(refreshJourney, 60_000);
window.setInterval(() => {
  if (!refreshInProgress) showRemainingApproach();
}, 15_000);
