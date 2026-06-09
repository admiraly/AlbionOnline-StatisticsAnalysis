"use strict";

const $ = (id) => document.getElementById(id);
const fmt = (n) => n.toLocaleString("en-US");

async function api(path, method = "GET", body) {
  const opts = { method, headers: {} };
  if (body !== undefined) {
    opts.headers["Content-Type"] = "application/json";
    opts.body = JSON.stringify(body);
  }
  const res = await fetch(path, opts);
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  return { ok: res.ok, status: res.status, data };
}

function setBanner(message) {
  const el = $("banner");
  if (message) {
    el.textContent = message;
    el.style.display = "block";
  } else {
    el.style.display = "none";
  }
}

function uptime(seconds) {
  if (seconds <= 0) return "0s";
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  return [h ? h + "h" : "", m ? m + "m" : "", s + "s"].filter(Boolean).join(" ");
}

function renderTopEvents(top) {
  const tbody = $("top-events").querySelector("tbody");
  $("top-empty").style.display = top.length ? "none" : "block";
  if (!top.length) { tbody.innerHTML = ""; return; }
  const max = Math.max(...top.map((e) => e.count));
  tbody.innerHTML = top
    .map((e) => {
      const pct = max ? Math.round((e.count / max) * 100) : 0;
      return `<tr>
        <td style="width:64px"><b>${e.code}</b></td>
        <td style="width:80px" class="muted">${fmt(e.count)}</td>
        <td><div class="barwrap"><div class="bar" style="width:${pct}%"></div></div></td>
      </tr>`;
    })
    .join("");
}

function renderRecent(recent) {
  const el = $("recent");
  if (!recent.length) { el.innerHTML = '<div class="empty">Waiting for traffic…</div>'; return; }
  el.innerHTML = recent
    .map((p) => {
      const t = new Date(p.timestampUtc).toLocaleTimeString("en-US", { hour12: false });
      return `<div><span class="muted">${t}</span> <span class="kind ${p.kind}">${p.kind}</span> code <b>${p.code}</b> <span class="muted">· ${p.paramCount} params</span></div>`;
    })
    .join("");
}

function renderStatus(s) {
  const badge = $("status-badge");
  badge.textContent = s.running ? "capturing" : "stopped";
  badge.classList.toggle("on", s.running);
  $("server").textContent = s.server || "Unknown";
  $("c-events").textContent = fmt(s.totals.events);
  $("c-requests").textContent = fmt(s.totals.requests);
  $("c-responses").textContent = fmt(s.totals.responses);
  $("c-uptime").textContent = uptime(s.uptimeSeconds);
  renderTopEvents(s.topEvents || []);
  renderRecent(s.recent || []);
  setBanner(s.lastError);
  $("btn-start").disabled = s.running;
  $("btn-stop").disabled = !s.running;
}

async function refreshStatus() {
  try {
    const { data } = await api("/api/status");
    if (data) renderStatus(data);
  } catch (e) {
    setBanner("Cannot reach the dashboard backend: " + e.message);
  }
}

async function refreshDevices() {
  const el = $("devices");
  try {
    const { ok, data } = await api("/api/devices");
    if (!ok) {
      el.innerHTML = `<li class="muted">${(data && data.error) || "libpcap unavailable"}</li>`;
      return;
    }
    if (!data.length) {
      el.innerHTML = '<li class="muted">No capture devices found (need CAP_NET_RAW).</li>';
      return;
    }
    el.innerHTML = data
      .map((d) => `<li>[${d.index}] <b>${d.name}</b> <span class="muted">(${d.identifier})</span></li>`)
      .join("");
  } catch (e) {
    el.innerHTML = `<li class="muted">${e.message}</li>`;
  }
}

$("btn-start").addEventListener("click", async () => {
  const body = { filter: $("filter").value.trim() || null, all: $("all").checked };
  await api("/api/capture/start", "POST", body);
  refreshStatus();
});

$("btn-stop").addEventListener("click", async () => {
  await api("/api/capture/stop", "POST");
  refreshStatus();
});

$("btn-replay").addEventListener("click", async () => {
  await api("/api/replay-sample", "POST");
  refreshStatus();
});

refreshDevices();
refreshStatus();
setInterval(refreshStatus, 1000);
