"use strict";

const $ = (id) => document.getElementById(id);
const fmt = (n) => (n ?? 0).toLocaleString("en-US");
const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const icon = (uniqueName, size = 64) => `https://render.albiononline.com/v1/item/${uniqueName}.png?size=${size}`;
const time = (iso) => new Date(iso).toLocaleTimeString("en-US", { hour12: false });

async function api(path, method = "GET", body) {
  const opts = { method, headers: {} };
  if (body !== undefined) { opts.headers["Content-Type"] = "application/json"; opts.body = JSON.stringify(body); }
  const res = await fetch(path, opts);
  const text = await res.text();
  return { ok: res.ok, status: res.status, data: text ? JSON.parse(text) : null };
}

function setBanner(message) {
  const el = $("banner");
  if (message) { el.textContent = message; el.style.display = "block"; }
  else { el.style.display = "none"; }
}

function uptime(seconds) {
  if (!seconds || seconds <= 0) return "0s";
  const h = Math.floor(seconds / 3600), m = Math.floor((seconds % 3600) / 60), s = seconds % 60;
  return [h ? h + "h" : "", m ? m + "m" : "", s + "s"].filter(Boolean).join(" ");
}

/* ---------- Overview ---------- */
function renderStatus(s) {
  const badge = $("status-badge");
  badge.textContent = s.running ? "capturing" : "stopped";
  badge.classList.toggle("on", s.running);
  $("server").textContent = s.server || "Unknown";
  $("c-events").textContent = fmt(s.totals.events);
  $("c-requests").textContent = fmt(s.totals.requests);
  $("c-responses").textContent = fmt(s.totals.responses);
  $("c-uptime").textContent = uptime(s.uptimeSeconds);

  const top = s.topEvents || [];
  $("top-empty").style.display = top.length ? "none" : "block";
  const max = Math.max(1, ...top.map((e) => e.count));
  $("top-events").querySelector("tbody").innerHTML = top
    .map((e) => `<tr><td style="width:64px"><b>${e.code}</b></td><td style="width:80px" class="muted">${fmt(e.count)}</td><td><div class="barwrap"><div class="bar" style="width:${Math.round((e.count / max) * 100)}%"></div></div></td></tr>`)
    .join("");

  const recent = s.recent || [];
  $("recent").innerHTML = recent.length
    ? recent.map((p) => `<div><span class="muted">${time(p.timestampUtc)}</span> <span class="kind ${p.kind}">${p.kind}</span> code <b>${p.code}</b> <span class="muted">· ${p.paramCount}p</span></div>`).join("")
    : '<div class="empty">Waiting for traffic…</div>';

  setBanner(s.lastError);
  $("btn-start").disabled = s.running;
  $("btn-stop").disabled = !s.running;
}

/* ---------- Damage ---------- */
function renderCombat(snap) {
  const entries = snap.entries || [];
  $("dmg-empty").style.display = entries.length ? "none" : "block";
  $("dm-meta").textContent = entries.length ? `${snap.durationSeconds}s · ${fmt(snap.totalDamage)} dmg` : "";
  $("dmg-table").querySelector("tbody").innerHTML = entries.map((e, i) => {
    const guild = e.guild ? ` <span class="muted">[${esc(e.guild)}]</span>` : "";
    const npc = e.isPlayer ? "" : ' <span class="npc">(npc)</span>';
    return `<tr><td class="rank">${i + 1}</td><td><b>${esc(e.name)}</b>${guild}${npc}</td>
      <td class="num dmg"><b>${fmt(e.damage)}</b><div class="barwrap"><div class="bar" style="width:${e.damagePercent}%"></div></div></td>
      <td class="num">${fmt(e.dps)}</td><td class="num">${e.damagePercent}%</td><td class="num heal">${e.healing ? "+" + fmt(e.healing) : ""}</td></tr>`;
  }).join("");
}

/* ---------- Loot ---------- */
function renderLoot(snap) {
  const items = snap.recent || [];
  $("loot-meta").textContent = (snap.itemEvents || snap.totalSilver) ? `${fmt(snap.itemEvents)} items · ${fmt(snap.totalSilver)} silver` : "";
  const el = $("loot-list");
  if (!items.length) { el.innerHTML = '<div class="empty">No loot yet.</div>'; return; }
  el.innerHTML = items.map((l) => {
    const from = l.lootedFrom ? ` <span class="muted">from ${esc(l.lootedFrom)}</span>` : "";
    if (l.isSilver) {
      return `<div class="itemrow"><div style="width:42px;text-align:center">🪙</div><div class="nm"><b>${esc(l.looter)}</b> looted <b>${fmt(l.quantity)}</b> silver${from}</div><div class="muted">${time(l.timestampUtc)}</div></div>`;
    }
    const img = l.uniqueName ? `<img src="${icon(l.uniqueName)}" alt="" loading="lazy" />` : '<div style="width:42px"></div>';
    const name = l.itemName ? esc(l.itemName) : `item #${l.itemIndex}`;
    return `<div class="itemrow">${img}<div class="nm"><b>${esc(l.looter)}</b> looted <b>${name}</b> ×${l.quantity}${from}</div><div class="muted">${time(l.timestampUtc)}</div></div>`;
  }).join("");
}

/* ---------- Gathering ---------- */
function renderGathering(snap) {
  const totals = snap.totals || [];
  const el = $("gathering-list");
  if (!totals.length) { el.innerHTML = '<div class="empty">No resources gathered yet.</div>'; return; }
  el.innerHTML = totals.map((t) => {
    const img = t.uniqueName ? `<img src="${icon(t.uniqueName)}" alt="" loading="lazy" />` : '<div style="width:42px"></div>';
    const tier = t.tier ? `<span class="tier">T${t.tier}</span> ` : "";
    return `<div class="itemrow">${img}<div class="nm">${tier}<b>${esc(t.name)}</b></div><div><b>${fmt(t.amount)}</b></div></div>`;
  }).join("");
}

/* ---------- Player ---------- */
function renderPlayer(s) {
  $("p-fame").textContent = fmt(s.fame);
  $("p-fameh").textContent = fmt(s.famePerHour) + "/h";
  $("p-silver").textContent = fmt(s.silver);
  $("p-silverh").textContent = fmt(s.silverPerHour) + "/h";
  $("p-might").textContent = fmt(s.might);
  $("p-favor").textContent = fmt(s.favor);
}

/* ---------- Party ---------- */
function renderParty(s) {
  const members = s.members || [];
  $("party-meta").textContent = members.length ? `${members.length} member(s)` : "";
  $("party-list").innerHTML = members.length ? members.map((m) => `<li>${esc(m)}</li>`).join("") : '<li class="empty">Not in a party.</li>';
}

/* ---------- Map ---------- */
function renderMap(snap) {
  const hist = snap.history || [];
  $("map-meta").textContent = snap.currentCluster ? `now: ${esc(snap.currentIsland || snap.currentCluster)}` : "";
  $("map-list").innerHTML = hist.length
    ? hist.map((v, i) => {
        const name = v.island ? esc(v.island) : esc(v.clusterId);
        const id = v.island ? ` <span class="muted">(${esc(v.clusterId)})</span>` : "";
        const here = i === 0 ? ' <span class="kind event">• here</span>' : "";
        return `<div><span class="muted">${time(v.enteredUtc)}</span> <b>${name}</b>${id} <span class="muted">${v.secondsInZone}s</span>${here}</div>`;
      }).join("")
    : '<div class="empty">No zone changes yet.</div>';
}

/* ---------- Devices ---------- */
async function refreshDevices() {
  const el = $("devices");
  try {
    const { ok, data } = await api("/api/devices");
    if (!ok) { el.innerHTML = `<li class="muted">${(data && data.error) || "libpcap unavailable"}</li>`; return; }
    el.innerHTML = data.length
      ? data.map((d) => `<li>[${d.index}] <b>${esc(d.name)}</b> <span class="muted">(${esc(d.identifier)})</span></li>`).join("")
      : '<li class="muted">No capture devices (need CAP_NET_RAW).</li>';
  } catch (e) { el.innerHTML = `<li class="muted">${esc(e.message)}</li>`; }
}

/* ---------- Item search + prices ---------- */
let searchTimer = null;
async function runSearch() {
  const q = $("item-q").value.trim();
  const el = $("item-results");
  if (q.length < 2) { el.innerHTML = '<div class="empty">Type at least 2 characters.</div>'; return; }
  try {
    const { data } = await api("/api/items/search?q=" + encodeURIComponent(q));
    if (!data || !data.length) { el.innerHTML = '<div class="empty">No items found.</div>'; return; }
    el.innerHTML = data.map((it) => `
      <div class="itemrow">
        <img src="${icon(it.uniqueName)}" alt="" loading="lazy" />
        <div class="nm"><b>${esc(it.name)}</b> ${it.tier ? `<span class="tier">T${it.tier}${it.enchantment ? "." + it.enchantment : ""}</span>` : ""}
          <div class="muted" style="font-size:11px">${esc(it.uniqueName)}</div>
          <div class="prices" id="px-${it.index}"></div>
        </div>
        <button class="ghost small" onclick="loadPrices('${esc(it.uniqueName)}', ${it.index})">Prices</button>
      </div>`).join("");
  } catch (e) { el.innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
}

async function loadPrices(uniqueName, index) {
  const box = $("px-" + index);
  if (!box) return;
  box.textContent = "loading prices…";
  const server = $("price-server").value;
  try {
    const { ok, data } = await api(`/api/items/prices?server=${server}&items=` + encodeURIComponent(uniqueName));
    if (!ok) { box.textContent = (data && data.error) || "price lookup failed"; return; }
    const withSell = (data || []).filter((p) => p.sellPriceMin > 0);
    if (!withSell.length) { box.innerHTML = '<span class="muted">no recent sell orders</span>'; return; }
    box.innerHTML = withSell.slice(0, 6).map((p) => `${esc(p.city)}: <b>${fmt(p.sellPriceMin)}</b>`).join(" · ");
  } catch (e) { box.textContent = esc(e.message); }
}
window.loadPrices = loadPrices;

/* ---------- polling + tabs ---------- */
async function poll(path, render) {
  try { const { data } = await api(path); if (data) render(data); } catch (e) { /* retry next tick */ }
}
const refreshStatus = () => poll("/api/status", renderStatus);
const refreshCombat = () => poll("/api/combat", renderCombat);
const refreshLoot = () => poll("/api/loot", renderLoot);
const refreshGathering = () => poll("/api/gathering", renderGathering);
const refreshPlayer = () => poll("/api/player", renderPlayer);
const refreshParty = () => poll("/api/party", renderParty);
const refreshMap = () => poll("/api/map", renderMap);

const tabRefreshers = { damage: refreshCombat, loot: refreshLoot, gathering: refreshGathering, player: refreshPlayer, party: refreshParty, map: refreshMap, devices: refreshDevices };
let activeTab = "overview";

function switchTab(name) {
  activeTab = name;
  document.querySelectorAll("nav.tabs button").forEach((b) => b.classList.toggle("active", b.dataset.tab === name));
  document.querySelectorAll("section.tab").forEach((s) => s.classList.toggle("active", s.dataset.tab === name));
  if (tabRefreshers[name]) tabRefreshers[name]();
  if (name === "items") $("item-q").focus();
}

document.querySelectorAll("nav.tabs button").forEach((b) => b.addEventListener("click", () => switchTab(b.dataset.tab)));

/* ---------- buttons ---------- */
const post = (path, then) => async () => { await api(path, "POST"); if (then) then(); };
$("btn-start").addEventListener("click", async () => { await api("/api/capture/start", "POST", { filter: $("filter").value.trim() || null, all: $("all").checked }); refreshStatus(); });
$("btn-stop").addEventListener("click", post("/api/capture/stop", refreshStatus));
$("btn-replay").addEventListener("click", post("/api/replay-sample", refreshStatus));
$("btn-replay-combat").addEventListener("click", post("/api/replay-combat", refreshCombat));
$("btn-reset-combat").addEventListener("click", post("/api/combat/reset", refreshCombat));
$("btn-replay-loot").addEventListener("click", post("/api/replay-loot", refreshLoot));
$("btn-reset-loot").addEventListener("click", post("/api/loot/reset", refreshLoot));
$("btn-replay-gathering").addEventListener("click", post("/api/replay-gathering", refreshGathering));
$("btn-reset-gathering").addEventListener("click", post("/api/gathering/reset", refreshGathering));
$("btn-replay-player").addEventListener("click", post("/api/replay-player", refreshPlayer));
$("btn-reset-player").addEventListener("click", post("/api/player/reset", refreshPlayer));
$("btn-replay-party").addEventListener("click", post("/api/replay-party", refreshParty));
$("btn-reset-party").addEventListener("click", post("/api/party/reset", refreshParty));
$("btn-replay-map").addEventListener("click", post("/api/replay-map", refreshMap));
$("btn-reset-map").addEventListener("click", post("/api/map/reset", refreshMap));
$("item-q").addEventListener("input", () => { clearTimeout(searchTimer); searchTimer = setTimeout(runSearch, 250); });

/* ---------- init ---------- */
refreshStatus();
refreshDevices();
setInterval(() => {
  refreshStatus();
  if (activeTab !== "overview" && tabRefreshers[activeTab] && activeTab !== "devices") tabRefreshers[activeTab]();
}, 1000);
