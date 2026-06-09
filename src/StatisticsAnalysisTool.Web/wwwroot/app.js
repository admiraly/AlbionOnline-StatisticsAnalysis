"use strict";

const $ = (id) => document.getElementById(id);
const fmt = (n) => n.toLocaleString("en-US");
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

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

function renderCombat(snap) {
  const tbody = $("dmg-table").querySelector("tbody");
  const entries = snap.entries || [];
  $("dmg-empty").style.display = entries.length ? "none" : "block";
  $("dm-meta").textContent = entries.length
    ? `${snap.durationSeconds}s · ${fmt(snap.totalDamage)} dmg`
    : "";
  tbody.innerHTML = entries
    .map((e, i) => {
      const guild = e.guild ? ` <span class="muted">[${esc(e.guild)}]</span>` : "";
      const npc = e.isPlayer ? "" : ' <span class="npc">(npc)</span>';
      const heal = e.healing ? "+" + fmt(e.healing) : "";
      return `<tr>
        <td class="rank">${i + 1}</td>
        <td><b>${esc(e.name)}</b>${guild}${npc}</td>
        <td class="num dmg"><b>${fmt(e.damage)}</b><div class="barwrap"><div class="bar" style="width:${e.damagePercent}%"></div></div></td>
        <td class="num">${fmt(e.dps)}</td>
        <td class="num">${e.damagePercent}%</td>
        <td class="num heal">${heal}</td>
      </tr>`;
    })
    .join("");
}

async function refreshCombat() {
  try {
    const { data } = await api("/api/combat");
    if (data) renderCombat(data);
  } catch (e) {
    /* transient; next tick retries */
  }
}

function time(iso) {
  return new Date(iso).toLocaleTimeString("en-US", { hour12: false });
}

function renderLoot(snap) {
  const el = $("loot-list");
  const items = snap.recent || [];
  $("loot-meta").textContent = (snap.itemEvents || snap.totalSilver)
    ? `${fmt(snap.itemEvents)} items · ${fmt(snap.totalSilver)} silver`
    : "";
  if (!items.length) { el.innerHTML = '<div class="empty">No loot yet.</div>'; return; }
  el.innerHTML = items
    .map((l) => {
      const what = l.isSilver ? `<span class="kind event">${fmt(l.quantity)} silver</span>` : `item #${l.itemIndex} ×${l.quantity}`;
      const from = l.lootedFrom ? ` <span class="muted">from ${esc(l.lootedFrom)}</span>` : "";
      return `<div><span class="muted">${time(l.timestampUtc)}</span> <b>${esc(l.looter)}</b> ${what}${from}</div>`;
    })
    .join("");
}

function renderMap(snap) {
  const el = $("map-list");
  const hist = snap.history || [];
  $("map-meta").textContent = snap.currentCluster ? `now: ${esc(snap.currentIsland || snap.currentCluster)}` : "";
  if (!hist.length) { el.innerHTML = '<div class="empty">No zone changes yet.</div>'; return; }
  el.innerHTML = hist
    .map((v, i) => {
      const name = v.island ? esc(v.island) : esc(v.clusterId);
      const id = v.island ? ` <span class="muted">(${esc(v.clusterId)})</span>` : "";
      const here = i === 0 ? ' <span class="kind event">• here</span>' : "";
      return `<div><span class="muted">${time(v.enteredUtc)}</span> <b>${name}</b>${id} <span class="muted">${v.secondsInZone}s</span>${here}</div>`;
    })
    .join("");
}

async function refreshLoot() {
  try { const { data } = await api("/api/loot"); if (data) renderLoot(data); } catch (e) { /* retry next tick */ }
}

async function refreshMap() {
  try { const { data } = await api("/api/map"); if (data) renderMap(data); } catch (e) { /* retry next tick */ }
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

$("btn-replay-combat").addEventListener("click", async () => {
  await api("/api/replay-combat", "POST");
  refreshCombat();
  refreshStatus();
});

$("btn-reset-combat").addEventListener("click", async () => {
  await api("/api/combat/reset", "POST");
  refreshCombat();
});

$("btn-replay-loot").addEventListener("click", async () => { await api("/api/replay-loot", "POST"); refreshLoot(); });
$("btn-reset-loot").addEventListener("click", async () => { await api("/api/loot/reset", "POST"); refreshLoot(); });
$("btn-replay-map").addEventListener("click", async () => { await api("/api/replay-map", "POST"); refreshMap(); });
$("btn-reset-map").addEventListener("click", async () => { await api("/api/map/reset", "POST"); refreshMap(); });

refreshDevices();
refreshStatus();
refreshCombat();
refreshLoot();
refreshMap();
setInterval(() => {
  refreshStatus();
  refreshCombat();
  refreshLoot();
  refreshMap();
}, 1000);
