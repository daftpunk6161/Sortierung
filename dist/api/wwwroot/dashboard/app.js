const state = {
  bootstrap: null,
  apiKey: sessionStorage.getItem("romulus.dashboard.apiKey") || "",
  selectedRunId: null,
  summaryInterval: null,
  streamAbort: null,
  activeStreamRunId: null
};

document.addEventListener("DOMContentLoaded", () => {
  bindUi();
  initialize().catch(showError);
});

function bindUi() {
  document.getElementById("connect-button").addEventListener("click", () => connectDashboard());
  document.getElementById("clear-key-button").addEventListener("click", clearApiKey);
  document.getElementById("refresh-button").addEventListener("click", () => refreshSummary());
  document.getElementById("run-form").addEventListener("submit", onRunSubmit);
  document.getElementById("cancel-run-button").addEventListener("click", cancelActiveRun);
  document.getElementById("update-dats-button").addEventListener("click", updateDats);
  document.getElementById("approve-visible-reviews-button").addEventListener("click", approveVisibleReviews);
}

async function initialize() {
  document.getElementById("api-key-input").value = state.apiKey;
  state.bootstrap = await fetchJson("/dashboard/bootstrap", {}, true);
  renderBootstrap();
  if (state.apiKey) {
    await connectDashboard();
  }
}

async function connectDashboard() {
  state.apiKey = document.getElementById("api-key-input").value.trim();
  if (!state.apiKey) {
    setConnectionState("API-Key fehlt.", true);
    return;
  }

  sessionStorage.setItem("romulus.dashboard.apiKey", state.apiKey);
  setConnectionState("Verbinde ...");
  await refreshSummary();

  if (state.summaryInterval) {
    clearInterval(state.summaryInterval);
  }

  state.summaryInterval = setInterval(() => {
    refreshSummary().catch(showError);
  }, 15000);

  setConnectionState("Verbunden.");
}

function clearApiKey() {
  sessionStorage.removeItem("romulus.dashboard.apiKey");
  state.apiKey = "";
  if (state.summaryInterval) {
    clearInterval(state.summaryInterval);
    state.summaryInterval = null;
  }
  if (state.streamAbort) {
    state.streamAbort.abort();
    state.streamAbort = null;
    state.activeStreamRunId = null;
  }
  document.getElementById("api-key-input").value = "";
  setConnectionState("API-Key entfernt.");
}

async function refreshSummary() {
  const summary = await fetchJson("/dashboard/summary");
  renderSummary(summary);
  if (summary.activeRun && summary.activeRun.runId) {
    connectRunStream(summary.activeRun.runId).catch(showError);
  } else {
    stopRunStream();
  }
}

async function onRunSubmit(event) {
  event.preventDefault();

  const payload = buildRunPayload();
  const response = await fetchJson("/runs?wait=false", {
    method: "POST",
    body: JSON.stringify(payload)
  });

  const run = response.run;
  if (!run || !run.runId) {
    throw new Error("Run wurde nicht erzeugt.");
  }

  state.selectedRunId = run.runId;
  appendLog(`[run] ${run.runId} gestartet`);
  await refreshSummary();
  await loadRunDetails(run.runId);
}

function buildRunPayload() {
  const roots = document.getElementById("roots-input").value
    .split(/\r?\n|;/)
    .map((value) => value.trim())
    .filter(Boolean);

  return {
    roots,
    mode: document.getElementById("mode-select").value,
    workflowScenarioId: emptyToNull(document.getElementById("workflow-select").value),
    profileId: emptyToNull(document.getElementById("profile-select").value),
    sortConsole: document.getElementById("sort-console-checkbox").checked,
    enableDat: document.getElementById("enable-dat-checkbox").checked,
    enableDatAudit: document.getElementById("dat-audit-checkbox").checked,
    enableDatRename: document.getElementById("dat-rename-checkbox").checked,
    onlyGames: document.getElementById("only-games-checkbox").checked,
    aggressiveJunk: document.getElementById("aggressive-junk-checkbox").checked,
    approveReviews: document.getElementById("approve-reviews-checkbox").checked,
    approveConversionReview: document.getElementById("approve-conversion-review-checkbox").checked,
    convertFormat: emptyToNull(document.getElementById("convert-select").value)
  };
}

async function cancelActiveRun() {
  const button = document.getElementById("cancel-run-button");
  const runId = button.dataset.runId;
  if (!runId) {
    return;
  }

  await fetchJson(`/runs/${runId}/cancel`, { method: "POST" });
  appendLog(`[run] ${runId} Abbruch angefordert`);
  await refreshSummary();
}

async function updateDats() {
  const result = await fetchJson("/dats/update", {
    method: "POST",
    body: JSON.stringify({ force: false })
  });

  appendLog(`[dat] update downloaded=${result.downloaded} skipped=${result.skipped} failed=${result.failed}`);
  await refreshSummary();
}

async function loadRunDetails(runId) {
  state.selectedRunId = runId;
  document.getElementById("selected-run-label").textContent = runId;

  const [resultEnvelope, reviewQueue, completeness] = await Promise.all([
    fetchJson(`/runs/${runId}/result`),
    fetchJson(`/runs/${runId}/reviews?limit=50`).catch((error) => ({ error: error.message })),
    fetchJson(`/runs/${runId}/completeness`).catch((error) => ({ error: error.message }))
  ]);

  renderRunResult(resultEnvelope.run, resultEnvelope.result);
  renderReviewQueue(reviewQueue);
  renderCompleteness(completeness);
}

async function approveVisibleReviews() {
  if (!state.selectedRunId) {
    return;
  }

  const items = Array.from(document.querySelectorAll("[data-review-path]"))
    .map((element) => element.getAttribute("data-review-path"))
    .filter(Boolean);

  if (items.length === 0) {
    return;
  }

  await fetchJson(`/runs/${state.selectedRunId}/reviews/approve`, {
    method: "POST",
    body: JSON.stringify({ paths: items })
  });

  appendLog(`[review] ${items.length} Eintraege freigegeben`);
  await loadRunDetails(state.selectedRunId);
}

function renderBootstrap() {
  const bootstrap = state.bootstrap;
  document.getElementById("bootstrap-summary").textContent =
    `${bootstrap.version} | Dashboard ${bootstrap.dashboardEnabled ? "aktiv" : "deaktiviert"} | ` +
    `Remote ${bootstrap.allowRemoteClients ? "freigegeben" : "lokal"}`;
}

function renderSummary(summary) {
  renderSummaryCards(summary);
  renderActiveRun(summary.activeRun);
  renderRunsTable(summary.recentRuns || []);
  renderDatStatus(summary.datStatus);
  populateSelect("profile-select", summary.profiles || [], "id", (item) => `${item.name} (${item.id})`);
  populateSelect("workflow-select", summary.workflows || [], "id", (item) => `${item.name} (${item.id})`);

  if (state.selectedRunId) {
    const stillPresent = (summary.recentRuns || []).some((run) => run.runId === state.selectedRunId)
      || (summary.activeRun && summary.activeRun.runId === state.selectedRunId);
    if (stillPresent) {
      loadRunDetails(state.selectedRunId).catch(showError);
    }
  }
}

function renderSummaryCards(summary) {
  const trends = summary.trends || {};
  const cards = [
    metricCard("Aktiver Run", summary.hasActiveRun ? "Ja" : "Nein", summary.activeRun ? summary.activeRun.status : "idle"),
    metricCard("Samples", String(trends.sampleCount || 0), "Persistierte Snapshot-Basis"),
    metricCard("Files", String((trends.totalFiles && trends.totalFiles.current) || 0), deltaText(trends.totalFiles)),
    metricCard("Collection", formatBytes((trends.collectionSizeBytes && trends.collectionSizeBytes.current) || 0), deltaText(trends.collectionSizeBytes)),
    metricCard("Health", `${(trends.healthScore && trends.healthScore.current) || 0}%`, deltaText(trends.healthScore)),
    metricCard("DAT Files", String(summary.datStatus ? summary.datStatus.totalFiles : 0), summary.datStatus && summary.datStatus.staleWarning ? summary.datStatus.staleWarning : "DAT Root Status"),
    metricCard("Profiles", String((summary.profiles || []).length), "Verfuegbar"),
    metricCard("Workflows", String((summary.workflows || []).length), "Verfuegbar")
  ];

  document.getElementById("summary-cards").innerHTML = cards.join("");
}

function renderActiveRun(run) {
  const card = document.getElementById("active-run-card");
  const cancelButton = document.getElementById("cancel-run-button");

  if (!run) {
    card.innerHTML = "<p class='empty-state'>Kein aktiver Run.</p>";
    cancelButton.disabled = true;
    cancelButton.dataset.runId = "";
    return;
  }

  cancelButton.disabled = false;
  cancelButton.dataset.runId = run.runId;
  card.innerHTML = `
    <div class="result-grid">
      <div><strong>${escapeHtml(run.runId)}</strong></div>
      <div>Status: ${escapeHtml(run.status)}</div>
      <div>Mode: ${escapeHtml(run.mode)}</div>
      <div>Progress: ${run.progressPercent}%</div>
      <div class="muted">${escapeHtml(run.progressMessage || "Keine Meldung")}</div>
    </div>`;
}

function renderRunsTable(runs) {
  if (!runs.length) {
    document.getElementById("runs-table-container").innerHTML = "<p class='empty-state'>Noch keine persisted Run-Historie.</p>";
    return;
  }

  const rows = runs.map((run) => `
    <tr data-run-id="${escapeAttr(run.runId)}">
      <td>${escapeHtml(run.runId)}</td>
      <td>${escapeHtml(run.status)}</td>
      <td>${escapeHtml(run.mode)}</td>
      <td>${escapeHtml(formatDate(run.completedUtc || run.startedUtc))}</td>
      <td>${run.totalFiles}</td>
      <td>${run.games}</td>
      <td>${run.datMatches}</td>
      <td>${run.convertedCount}</td>
      <td>${run.healthScore}%</td>
    </tr>`);

  document.getElementById("runs-table-container").innerHTML = `
    <table>
      <thead>
        <tr>
          <th>Run</th>
          <th>Status</th>
          <th>Mode</th>
          <th>Completed</th>
          <th>Files</th>
          <th>Games</th>
          <th>DAT</th>
          <th>Convert</th>
          <th>Health</th>
        </tr>
      </thead>
      <tbody>${rows.join("")}</tbody>
    </table>`;

  document.querySelectorAll("[data-run-id]").forEach((row) => {
    row.addEventListener("click", () => loadRunDetails(row.dataset.runId).catch(showError));
  });
}

function renderDatStatus(datStatus) {
  if (!datStatus) {
    document.getElementById("dat-status-card").innerHTML = "<p class='empty-state'>Keine DAT-Informationen.</p>";
    return;
  }

  const consoles = (datStatus.consoles || []).slice(0, 12).map((entry) => `
    <tr>
      <td>${escapeHtml(entry.console)}</td>
      <td>${entry.fileCount}</td>
      <td>${escapeHtml(formatDate(entry.newestFileUtc))}</td>
    </tr>`).join("");

  document.getElementById("dat-status-card").innerHTML = `
    <div class="result-grid">
      <div>Configured: <strong>${datStatus.configured ? "Ja" : "Nein"}</strong></div>
      <div>Root: <span class="${datStatus.withinAllowedRoots ? "" : "tag-danger"}">${escapeHtml(datStatus.datRoot || "-")}</span></div>
      <div>Total Files: ${datStatus.totalFiles}</div>
      <div>Catalog Entries: ${datStatus.catalogEntries}</div>
      <div>Old Files: ${datStatus.oldFileCount}</div>
      <div class="muted">${escapeHtml(datStatus.message || datStatus.staleWarning || "DAT Root vorhanden")}</div>
    </div>
    <div class="table-shell">
      <table>
        <thead><tr><th>Console</th><th>Files</th><th>Newest</th></tr></thead>
        <tbody>${consoles || "<tr><td colspan='3'>Keine Konsolenstatistik.</td></tr>"}</tbody>
      </table>
    </div>`;
}

function renderRunResult(run, result) {
  if (!run || !result) {
    document.getElementById("run-result-card").innerHTML = "<p class='empty-state'>Noch kein Result.</p>";
    return;
  }

  document.getElementById("run-result-card").innerHTML = `
    <div class="result-grid">
      <div><strong>${escapeHtml(run.runId)}</strong> | ${escapeHtml(run.status)}</div>
      <div>Files ${result.totalFiles} | Candidates ${result.candidates} | Winners ${result.winners} | Losers ${result.losers}</div>
      <div>Games ${result.games} | Junk ${result.junk} | Unknown ${result.unknown} | DAT ${result.datMatches}</div>
      <div>Converted ${result.convertedCount} | Blocked ${result.convertBlockedCount} | Review ${result.convertReviewCount}</div>
      <div>Saved ${formatBytes(result.savedBytes || 0)} | Convert Saved ${formatBytes(result.convertSavedBytes || 0)}</div>
      <div class="muted">${escapeHtml(result.orchestratorStatus || "")}</div>
    </div>`;
}

function renderReviewQueue(queue) {
  const button = document.getElementById("approve-visible-reviews-button");
  if (!queue || queue.error) {
    button.disabled = true;
    document.getElementById("review-queue").innerHTML = `<p class="empty-state">${escapeHtml(queue && queue.error ? queue.error : "Keine Review-Daten.")}</p>`;
    return;
  }

  const items = queue.items || [];
  button.disabled = items.length === 0;

  if (!items.length) {
    document.getElementById("review-queue").innerHTML = "<p class='empty-state'>Keine offenen Review-Eintraege.</p>";
    return;
  }

  document.getElementById("review-queue").innerHTML = items.map((item) => `
    <article class="review-item ${item.approved ? "approved" : ""}" data-review-path="${escapeAttr(item.mainPath)}">
      <div><strong>${escapeHtml(item.fileName)}</strong></div>
      <div class="review-meta">${escapeHtml(item.consoleKey)} | ${escapeHtml(item.sortDecision)} | ${escapeHtml(item.matchLevel)}</div>
      <div class="muted">${escapeHtml(item.matchReasoning || "")}</div>
    </article>`).join("");
}

function renderCompleteness(completeness) {
  if (!completeness || completeness.error) {
    document.getElementById("completeness-table").innerHTML = `<p class="empty-state">${escapeHtml(completeness && completeness.error ? completeness.error : "Keine Completeness-Daten.")}</p>`;
    return;
  }

  const entries = completeness.entries || [];
  if (!entries.length) {
    document.getElementById("completeness-table").innerHTML = "<p class='empty-state'>Keine Completeness-Eintraege.</p>";
    return;
  }

  const rows = entries.map((entry) => `
    <tr>
      <td>${escapeHtml(entry.consoleKey)}</td>
      <td>${entry.totalInDat}</td>
      <td>${entry.verified}</td>
      <td>${entry.missingCount}</td>
      <td>${entry.percentage}%</td>
    </tr>`).join("");

  document.getElementById("completeness-table").innerHTML = `
    <div class="muted">Quelle: ${escapeHtml(completeness.source || "-")} | Items: ${completeness.sourceItemCount || 0}</div>
    <table>
      <thead><tr><th>Console</th><th>Total DAT</th><th>Verified</th><th>Missing</th><th>%</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>`;
}

async function connectRunStream(runId) {
  if (!runId || state.activeStreamRunId === runId) {
    return;
  }

  stopRunStream();
  state.activeStreamRunId = runId;
  state.streamAbort = new AbortController();

  const response = await fetch(`/runs/${encodeURIComponent(runId)}/stream`, {
    headers: authHeaders(),
    signal: state.streamAbort.signal
  });

  if (!response.ok || !response.body) {
    throw new Error(`SSE Verbindung fehlgeschlagen (${response.status}).`);
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { value, done } = await reader.read();
    if (done) {
      break;
    }

    buffer += decoder.decode(value, { stream: true });
    const chunks = buffer.split("\n\n");
    buffer = chunks.pop() || "";

    for (const chunk of chunks) {
      const event = parseSseChunk(chunk);
      if (event) {
        handleSseEvent(event.name, event.payload);
      }
    }
  }
}

function stopRunStream() {
  if (state.streamAbort) {
    state.streamAbort.abort();
    state.streamAbort = null;
  }
  state.activeStreamRunId = null;
}

function handleSseEvent(eventName, payload) {
  appendLog(`[${eventName}] ${JSON.stringify(payload)}`);

  if (eventName === "status" && payload.runId) {
    renderActiveRun(payload);
    return;
  }

  if (["completed", "completed_with_errors", "cancelled", "failed"].includes(eventName)) {
    if (payload.run) {
      renderActiveRun(null);
      state.selectedRunId = payload.run.runId;
      loadRunDetails(payload.run.runId).catch(showError);
    }
    refreshSummary().catch(showError);
    stopRunStream();
  }
}

function parseSseChunk(chunk) {
  const lines = chunk.split("\n");
  let eventName = "message";
  const dataParts = [];

  for (const line of lines) {
    if (line.startsWith("event:")) {
      eventName = line.slice(6).trim();
    } else if (line.startsWith("data:")) {
      dataParts.push(line.slice(5).trim());
    }
  }

  if (!dataParts.length) {
    return null;
  }

  try {
    return { name: eventName, payload: JSON.parse(dataParts.join("\n")) };
  } catch {
    return { name: eventName, payload: { raw: dataParts.join("\n") } };
  }
}

function populateSelect(elementId, items, valueKey, labelFactory) {
  const select = document.getElementById(elementId);
  const currentValue = select.value;
  const options = [`<option value="">${elementId === "profile-select" ? "Keins" : "Keiner"}</option>`]
    .concat((items || []).map((item) =>
      `<option value="${escapeAttr(item[valueKey])}">${escapeHtml(labelFactory(item))}</option>`));
  select.innerHTML = options.join("");
  if (currentValue) {
    select.value = currentValue;
  }
}

function metricCard(label, value, detail) {
  return `
    <article class="metric-card">
      <div class="metric-label">${escapeHtml(label)}</div>
      <div class="metric-value">${escapeHtml(value)}</div>
      <div class="muted">${escapeHtml(detail || "")}</div>
    </article>`;
}

function authHeaders() {
  return {
    "Content-Type": "application/json",
    "X-Api-Key": state.apiKey
  };
}

async function fetchJson(path, options = {}, anonymous = false) {
  const headers = new Headers(options.headers || {});
  if (!anonymous) {
    if (!state.apiKey) {
      throw new Error("API-Key fehlt.");
    }
    headers.set("X-Api-Key", state.apiKey);
  }
  if (options.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(path, {
    ...options,
    headers
  });

  const text = await response.text();
  const data = text ? JSON.parse(text) : {};
  if (!response.ok) {
    const message = data && data.error && data.error.message
      ? data.error.message
      : `${response.status} ${response.statusText}`;
    throw new Error(message);
  }

  return data;
}

function appendLog(line) {
  const log = document.getElementById("active-run-log");
  const stamp = new Date().toISOString();
  log.textContent = `${log.textContent}\n${stamp} ${line}`.trim();
  log.scrollTop = log.scrollHeight;
}

function showError(error) {
  setConnectionState(error.message || String(error), true);
  appendLog(`[error] ${error.message || String(error)}`);
}

function setConnectionState(message, isError = false) {
  const element = document.getElementById("connection-state");
  element.textContent = message;
  element.className = isError ? "status-line tag-danger" : "status-line";
}

function formatBytes(bytes) {
  const value = Number(bytes || 0);
  if (!value) {
    return "0 B";
  }

  const units = ["B", "KB", "MB", "GB", "TB"];
  let size = value;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit += 1;
  }
  return `${size.toFixed(size >= 10 || unit === 0 ? 0 : 1)} ${units[unit]}`;
}

function formatDate(value) {
  if (!value) {
    return "-";
  }
  return new Date(value).toLocaleString("de-CH");
}

function deltaText(metric) {
  if (!metric) {
    return "";
  }
  const delta = Number(metric.delta || 0);
  return delta === 0 ? "Delta 0" : `Delta ${delta > 0 ? "+" : ""}${delta}`;
}

function emptyToNull(value) {
  return value ? value : null;
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll("\"", "&quot;")
    .replaceAll("'", "&#39;");
}

function escapeAttr(value) {
  return escapeHtml(value).replaceAll("`", "&#96;");
}
