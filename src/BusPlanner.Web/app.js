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
const languageSelect = document.querySelector("#language-select");
const pageUrl = new URL(window.location.href);
const pageParams = pageUrl.searchParams;
const journeyKey = pageParams.get("journey");
const debugMode = pageParams.get("debug") === "1";
const urlLanguage = ["en", "zh"].includes((pageParams.get("lang") || "").toLowerCase())
  ? pageParams.get("lang").toLowerCase()
  : null;
const savedLanguage = ["en", "zh"].includes(localStorage.getItem("busplanner-language"))
  ? localStorage.getItem("busplanner-language")
  : null;
const translations = {
  en: {
    language: "Language", system: "System", preparing: "Preparing journey…", updating: "Updating…", updated: "Updated just now", error: "Could not update",
    eyebrow: "Your connection, at a glance", yourJourney: "Your journey", checking: "Checking the next buses and the transfer.",
    journeyAdjustments: "Journey adjustments", starting: "I’m starting", firstStop: "First stop", transferStop: "Transfer stop",
    approachLabel: "Time until I can board", interchangeTime: "Interchange time", minutes: "min", refresh: "Refresh",
    catchableOptions: "Catchable options", nextDepartures: "Next departures", loadingSchedule: "Loading schedule…",
    measuring: "Measuring page and API response time…", loadingOptions: "Loading the next two options…", disclaimer: "Times are estimates. Check official service information before travelling.",
    refreshAuto: "The approach time counts down; results refresh every minute.", refreshTransfer: "The time to board counts down while this page is open.",
    ariaApproach: "Minutes until reaching the first boarding stop", ariaTransfer: "Minutes until ready to board at the selected transfer stop",
    livePrediction: "Live prediction", timetable: "Timetable", depart: "Depart", arrive: "Arrive", readyAfterTransfer: "Ready to board after transfer",
    connection1: "Connection 1", connectionBackup: n => `Connection ${n} · backup`, nextBus: "Next bus", followingBus: "Following bus · backup",
    primary: "Primary", backup: "Backup", firstLeg: "First leg", firstLegBackup: "First leg · backup",
    noService: "No catchable service was found for this starting point and remaining time.", alertsUnavailable: "Service alerts could not be loaded; trip and timetable data are still shown.",
    liveFeed: "Live feed", staleFeed: "Stale feed", scheduleFallback: "Schedule fallback", feedUpdated: "Feed updated", noTimestamp: "No feed timestamp",
    noConnection: "No feasible onward service from {stop} after the transfer allowance.", minutesUntil: "{stop} · {minutes} min until ready to board", reachStop: "{stop} · {minutes} min to reach the stop",
    serverError: "Journey data could not be loaded.", firstResultWaiting: "First result: waiting",
    firstResult: n => `First result: ${n} s`, latestApi: n => `Latest API round trip: ${n} ms`, render: n => `Render: ${n} ms`,
    staleNotice: "The real-time feed is stale; estimates use the timetable where available.", noRealtimeNotice: "Fresh real-time data is unavailable; estimates use the timetable where available.",
    noFirstBusNotice: "No eligible first-leg service is catchable after the remaining approach time.", oneFirstBusNotice: "Only one eligible first-leg service is currently catchable.",
    stopMoved: "A configured stop has moved.", detour: "A detour affects this service.", modifiedService: "Modified service affects this trip.", genericAlert: "A service alert affects this trip."
  },
  zh: {
    language: "语言", system: "跟随设备", preparing: "正在准备行程…", updating: "正在更新…", updated: "刚刚更新", error: "更新失败",
    eyebrow: "换乘信息，一目了然", yourJourney: "您的行程", checking: "正在查询下一班公交和换乘信息。",
    journeyAdjustments: "行程设置", starting: "我从这里出发", firstStop: "起点站", transferStop: "换乘站",
    approachLabel: "需多长时间到达车站", interchangeTime: "换乘预留时间", minutes: "分钟", refresh: "刷新",
    catchableOptions: "可赶上的班次", nextDepartures: "下一班公交", loadingSchedule: "正在加载时刻表…",
    measuring: "正在计算页面和接口响应时间…", loadingOptions: "正在查询接下来两班车…", disclaimer: "时间为估算值。出行前请查看官方服务信息。",
    refreshAuto: "到站时间会倒计时；结果每分钟自动更新。", refreshTransfer: "页面打开期间，可上车时间会倒计时。",
    ariaApproach: "距离到达起点站的分钟数", ariaTransfer: "到达换乘站后可上车还需的分钟数",
    livePrediction: "实时预测", timetable: "时刻表", depart: "出发", arrive: "到达", readyAfterTransfer: "换乘后可上车时间",
    connection1: "换乘班次 1", connectionBackup: n => `换乘班次 ${n} · 备选`, nextBus: "下一班", followingBus: "再下一班 · 备选",
    primary: "首选", backup: "备选", firstLeg: "第一程", firstLegBackup: "第一程 · 备选",
    noService: "从当前起点和剩余时间出发，没有找到可赶上的班次。", alertsUnavailable: "暂时无法读取服务公告；仍显示线路和时刻表信息。",
    liveFeed: "实时信息", staleFeed: "信息较旧", scheduleFallback: "仅时刻表", feedUpdated: "信息更新时间", noTimestamp: "暂无信息时间",
    noConnection: "换乘预留时间之后，{stop}没有可衔接的公交。", minutesUntil: "{stop} · 还需 {minutes} 分钟可上车", reachStop: "{stop} · 还需 {minutes} 分钟到达车站",
    serverError: "无法加载行程信息。", firstResultWaiting: "首个结果：等待中",
    firstResult: n => `首个结果：${n}秒`, latestApi: n => `最近接口往返：${n}毫秒`, render: n => `页面渲染：${n}毫秒`,
    staleNotice: "实时信息已过期；可用时将使用时刻表估算。", noRealtimeNotice: "暂时没有可用的实时信息；可用时将使用时刻表估算。",
    noFirstBusNotice: "按照当前剩余到站时间，没有可赶上的第一程公交。", oneFirstBusNotice: "目前只能赶上一个第一程班次。",
    stopMoved: "所选站点已迁移。", detour: "此班次受临时绕行影响。", modifiedService: "此班次的服务有所调整。", genericAlert: "此班次有服务公告。"
  }
};
const chineseTitleTerms = new Map([
  ["aldi", "附近德国店"], ["brownsplains", "布朗平原"], ["browns plains", "布朗平原"], ["bshs", "布里斯班州立高中"], ["churchie", "Churchie学校"], ["home", "家"],
  ["work", "工作地点"], ["run feng", "润丰"], ["runfeng", "润丰"], ["school", "学校"]
]);
const chineseStops = new Map([
  ["Heidelberg St at Heath Street, stop 15", "海德堡街与希思街路口（Heidelberg St at Heath Street）15号站"],
  ["Mater Hill station, platform 1", "Mater Hill车站1号站台"],
  ["Mater Hill station, platform 2", "Mater Hill车站2号站台"],
  ["Honeysuckle Way at Sunflower, Calamvale", "Calamvale区Sunflower旁的Honeysuckle Way站"],
  ["Edward Street Stop 145, Brisbane City", "布里斯班市Edward Street 145号站"],
  ["Compton Rd at Frizzell Street, stop 85", "Compton Road与Frizzell Street路口85号站"],
  ["Gowan Rd at Gowan and Compton", "Gowan Road与Compton Road路口站"]
]);
let language = urlLanguage || savedLanguage || browserPreferredLanguage();
let latestResponse = null;
let approachSetAt = Date.now();
let approachBaseMinutes = 10;
let defaultOriginApproachMinutes = 10;
let firstResponse = true;
let firstResultElapsedMs = null;
let refreshInProgress = false;
let journeyHasTransfers = false;
let pageMetrics = null;
performanceElement.hidden = !debugMode;

function renderPerformance() {
  if (!pageMetrics) return;
  const first = pageMetrics.first === null
    ? tr("firstResultWaiting")
    : tr("firstResult", { number: (pageMetrics.first / 1000).toFixed(2) });
  performanceElement.textContent = `${first} · ${tr("latestApi", { number: pageMetrics.api })} · ${tr("render", { number: pageMetrics.render })}`;
}

function selectedApproachMinutes() {
  const elapsed = Math.floor((Date.now() - approachSetAt) / 60_000);
  return Math.max(0, approachBaseMinutes - elapsed);
}

function tr(key, values = {}) {
  const value = translations[language][key] ?? translations.en[key] ?? key;
  return (typeof value === "function" ? value(values.number) : value).replace?.(/\{(\w+)\}/g, (_, name) => values[name] ?? "") ?? value;
}

function browserPreferredLanguage() {
  const preferred = navigator.languages?.[0] || navigator.language || "";
  return /^zh(-|$)/i.test(preferred) ? "zh" : "en";
}

function displayStop(name) {
  return language === "zh" ? chineseStops.get(name) || name : name;
}

function displayJourneyLabel(label) {
  if (!label || language !== "zh") return label || "BusPlanner";
  return label.split(/\s*(?:->|→|–|—)\s*/).map(part => {
    const trimmed = part.trim();
    const translation = chineseTitleTerms.get(trimmed.toLowerCase());
    return translation || trimmed;
  }).join(" → ");
}

function translateStatus(message) {
  if (language !== "zh") return message;
  const match = message.match(/^No feasible onward service from (.+) after the transfer allowance\.?$/);
  if (match) return tr("noConnection", { stop: displayStop(match[1]) });
  const known = new Map([
    ["The real-time feed is stale; estimates use the timetable where available.", "staleNotice"],
    ["Fresh real-time data is unavailable; estimates use the timetable where available.", "noRealtimeNotice"],
    ["No eligible first-leg service is catchable after the remaining approach time.", "noFirstBusNotice"],
    ["Only one eligible first-leg service is currently catchable.", "oneFirstBusNotice"],
    ["A configured stop has moved.", "stopMoved"], ["A detour affects this service.", "detour"],
    ["Modified service affects this trip.", "modifiedService"], ["A service alert affects this trip.", "genericAlert"]
  ]);
  if (known.has(message)) return tr(known.get(message));
  return message;
}

function applyLanguage() {
  language = languageSelect.value === "system" ? browserPreferredLanguage() : languageSelect.value;
  document.documentElement.lang = language === "zh" ? "zh-CN" : "en-AU";
  for (const element of document.querySelectorAll("[data-i18n]")) element.textContent = tr(element.dataset.i18n);
  for (const element of document.querySelectorAll("[data-i18n-aria]")) element.setAttribute("aria-label", tr(element.dataset.i18nAria));
  languageSelect.setAttribute("aria-label", tr("language"));
  connectionState.textContent = tr(connectionState.dataset.state === "ready" ? "updated" : connectionState.dataset.state === "error" ? "error" : connectionState.dataset.state === "loading" ? "updating" : "preparing");
  if (latestResponse) renderResults(latestResponse);
  else {
    connectionState.textContent = tr("preparing");
    feedStatusElement.textContent = tr("loadingSchedule");
    performanceElement.textContent = tr("measuring");
    resultElement.firstElementChild.textContent = tr("loadingOptions");
  }
  renderPerformance();
  syncControls();
}

function chooseLanguage() {
  const selected = languageSelect.value;
  if (selected === "system") {
    localStorage.removeItem("busplanner-language");
    pageParams.delete("lang");
  } else {
    localStorage.setItem("busplanner-language", selected);
    pageParams.set("lang", selected);
  }
  history.replaceState(null, "", `${pageUrl.pathname}${pageUrl.search}${pageUrl.hash}`);
  applyLanguage();
}

function showRemainingApproach() {
  approachInput.value = String(selectedApproachMinutes());
}

function syncControls() {
  const atInterchange = stageInput.value !== "origin";
  stageInput.closest(".control-row").hidden = stageInput.options.length <= 1;
  transferControl.hidden = atInterchange || !journeyHasTransfers;
  refreshNote.textContent = atInterchange ? tr("refreshTransfer") : tr("refreshAuto");
  approachInput.setAttribute("aria-label", tr(atInterchange ? "ariaTransfer" : "ariaApproach"));
}

function formatTime(value) {
  if (!value) return "—";
  return new Intl.DateTimeFormat(language === "zh" ? "zh-CN" : "en-AU", {
    timeZone: "Australia/Brisbane",
    hour: "numeric",
    minute: "2-digit"
  }).format(new Date(value));
}

function sourceLabel(source) {
  return source === "realtime" ? tr("livePrediction") : tr("timetable");
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
  route.textContent = language === "zh" ? `${trip.routeShortName}路公交` : `Route ${trip.routeShortName}`;
  const direction = document.createElement("div");
  direction.className = "trip-direction";
  direction.textContent = trip.headSign
    ? (language === "zh" ? trip.headSign.replace(/Fortitude Valley/gi, "Fortitude Valley（福蒂特谷）").replace(/Brisbane City/gi, "布里斯班市") : trip.headSign)
    : `${displayStop(trip.boardStopName)} → ${displayStop(trip.alightStopName)}`;
  routeBlock.append(route, direction);
  const kind = document.createElement("span");
  kind.className = "trip-kind";
  kind.textContent = optionLabel;
  heading.append(routeBlock, kind);

  const times = document.createElement("div");
  times.className = "trip-time-row";
  times.append(
    makeTimePair(`${tr("depart")} · ${displayStop(trip.boardStopName)}`, trip.estimatedDeparture, trip.departureSource),
    makeTimePair(`${tr("arrive")} · ${displayStop(trip.alightStopName)}`, trip.estimatedArrival, trip.arrivalSource)
  );
  container.append(heading, times);

  if (trip.transferReadyAt) {
    const ready = document.createElement("p");
    ready.className = "trip-kind";
    ready.textContent = `${tr("readyAfterTransfer")}: ${formatTime(trip.transferReadyAt)}`;
    container.append(ready);
  }

  if (branch.statusMessage) {
    const status = document.createElement("p");
    status.className = "branch-status";
    status.textContent = translateStatus(branch.statusMessage);
    container.append(status);
  }

  if (trip.warnings?.length) {
    const warning = document.createElement("p");
    warning.className = "trip-warnings";
    warning.textContent = trip.warnings.map(translateStatus).join(" · ");
    container.append(warning);
  }

  for (const [index, next] of (branch.nextLegOptions || []).entries()) {
    container.append(renderTrip(next, index === 0 ? tr("connection1") : tr("connectionBackup", { number: index + 1 }), depth + 1));
  }
  return container;
}

function renderResults(response) {
  latestResponse = response;
  const plan = response.plan;
  const selectedStage = stageInput.value;
  stageInput.replaceChildren(new Option(tr("firstStop"), "origin"));
  for (const stage of response.availableStartingStages || []) {
    stageInput.add(new Option(displayStop(stage.label), stage.key));
  }
  if ([...stageInput.options].some(option => option.value === selectedStage)) stageInput.value = selectedStage;
  journeyHasTransfers = response.hasTransfers;
  syncControls();
  titleElement.textContent = displayJourneyLabel(response.journeyLabel);
  if (response.returnJourneyQuickLink && response.returnJourneyLabel) {
    const returnParams = new URLSearchParams({ journey: response.returnJourneyQuickLink });
    if (languageSelect.value !== "system") returnParams.set("lang", language);
    if (debugMode) returnParams.set("debug", "1");
    returnJourneyLink.href = `/?${returnParams.toString()}`;
    returnJourneyLink.textContent = displayJourneyLabel(response.returnJourneyLabel);
    returnJourneyLink.hidden = false;
  } else {
    returnJourneyLink.hidden = true;
  }
  subtitleElement.textContent = response.startingStage !== "origin"
    ? tr("minutesUntil", { stop: displayStop(response.startingStopName), minutes: response.approachMinutes })
    : tr("reachStop", { stop: displayStop(response.startingStopName), minutes: response.approachMinutes });

  const freshness = (plan.feedFreshness || "unavailable").toLowerCase();
  feedStatusElement.dataset.freshness = freshness;
  const updated = plan.feedTimestamp ? `${tr("feedUpdated")} ${formatTime(plan.feedTimestamp)}` : tr("noTimestamp");
  feedStatusElement.textContent = `${tr(freshness === "fresh" ? "liveFeed" : freshness === "stale" ? "staleFeed" : "scheduleFallback")} · ${updated}`;

  const notices = [...(plan.notices || [])];
  if (!response.alertsAvailable) notices.push(tr("alertsUnavailable"));
  noticeElement.hidden = notices.length === 0;
  noticeElement.replaceChildren();
  if (notices.length) {
    const list = document.createElement("ul");
    for (const message of notices) {
      const item = document.createElement("li");
      item.textContent = translateStatus(message);
      list.append(item);
    }
    noticeElement.append(list);
  }

  resultElement.replaceChildren();
  if (!plan.firstLegOptions?.length) {
    const empty = document.createElement("div");
    empty.className = "card empty-card";
    empty.textContent = tr("noService");
    resultElement.append(empty);
    return;
  }

  plan.firstLegOptions.forEach((branch, index) => {
    const card = document.createElement("section");
    card.className = "card option-card";
    const banner = document.createElement("div");
    banner.className = "option-banner";
    const title = document.createElement("span");
    title.textContent = index === 0 ? tr("nextBus") : tr("followingBus");
    const tag = document.createElement("span");
    tag.className = "tag";
    tag.textContent = index === 0 ? tr("primary") : tr("backup");
    banner.append(title, tag);
    card.append(banner, renderTrip(branch, index === 0 ? tr("firstLeg") : tr("firstLegBackup")));
    resultElement.append(card);
  });
}

async function refreshJourney() {
  if (refreshInProgress) return;
  refreshInProgress = true;
  refreshButton.disabled = true;
  connectionState.textContent = tr("updating");
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
    pageMetrics = { first: firstResultElapsedMs, api: Math.round(apiElapsedMs), render: Math.round(renderElapsedMs) };
    renderPerformance();
    performanceElement.dataset.withinTarget = String(firstResultElapsedMs !== null && firstResultElapsedMs < 2000);
    connectionState.textContent = tr("updated");
    connectionState.dataset.state = "ready";
  } catch (error) {
    resultElement.replaceChildren();
    const card = document.createElement("div");
    card.className = "card error-card";
    card.textContent = language === "zh" ? tr("serverError") : error instanceof Error ? error.message : tr("serverError");
    resultElement.append(card);
    connectionState.textContent = tr("error");
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
languageSelect.value = urlLanguage || savedLanguage || "system";
languageSelect.addEventListener("change", chooseLanguage);
window.addEventListener("languagechange", () => {
  if (languageSelect.value === "system") applyLanguage();
});

applyLanguage();
syncControls();
refreshJourney();
window.setInterval(refreshJourney, 60_000);
window.setInterval(() => {
  if (!refreshInProgress) showRemainingApproach();
}, 15_000);
