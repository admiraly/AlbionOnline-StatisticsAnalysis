# Linux Port — Roadmap & Architecture

This document tracks the effort to make **AlbionOnline-StatisticsAnalysis** usable on
Linux. Upstream is a Windows-only **WPF** desktop app (`net10.0-windows`); this fork keeps
that app intact and adds a **cross-platform engine + web dashboard** that run natively on
Linux (and Windows/macOS).

## Why this approach

The codebase already separates cleanly:

| Layer | Projects | Portable today? |
|-------|----------|-----------------|
| Packet parsing | `PhotonPackageParser`, `Protocol18`, `Network`, `Abstractions`, `Diagnostics` | ✅ `net10.0`, no WPF |
| Game-data extraction | `StatisticAnalysisTool.Extractor` | ✅ `net10.0` |
| **GUI** | `StatisticsAnalysisTool` (WPF) | ❌ `net10.0-windows`, 57 XAML files, WebView2/OpenGL/etc. |

The capture→parse engine is already WPF-free. `LibpcapPacketProvider` uses the
cross-platform **libpcap** binding (libpcap is native to Linux), with zero `System.Windows`
references. The only thing locking the app to Windows is the **presentation layer**.

So instead of porting 57 WPF XAML views to Avalonia (weeks, hard to verify headless), we:

1. Lift the WPF-free **capture + parse + tracking engine** into a new cross-platform library
   (`StatisticsAnalysisTool.Core`).
2. Put a cross-platform **ASP.NET Core web dashboard** on top of it
   (`StatisticsAnalysisTool.Web`) — a browser UI with live updates (SignalR).

The original Windows WPF app is **left untouched** so it keeps building and working. New code
is additive; shared logic is unified over time (see "De-duplication" below).

## Target architecture

```
                 ┌─────────────────────────────────────────────┐
                 │  StatisticsAnalysisTool.Web (ASP.NET Core)   │  ← runs on Linux
                 │  REST + SignalR + static SPA dashboard       │
                 └───────────────────────┬─────────────────────┘
                                         │
                 ┌───────────────────────▼─────────────────────┐
                 │  StatisticsAnalysisTool.Core (net10.0)       │
                 │  • capture (libpcap) + server detection      │
                 │  • settings abstraction (no static globals)  │
                 │  • feature trackers (damage/loot/dungeon/…)  │
                 └───────────────────────┬─────────────────────┘
                                         │ project refs
   ┌─────────────────────────────────────┼──────────────────────────────────┐
   ▼                 ▼                    ▼                ▼                   ▼
 Network        PhotonPackageParser   Protocol18      Abstractions       Diagnostics
 (all already net10.0, cross-platform — reused as-is)
```

## Capture on Linux

- **libpcap** is the capture path on Linux (the Windows raw-socket `SIO_RCVALL` provider does
  not apply). Albion uses the Photon protocol over UDP ports 5055/5056/5058.
- Requires raw-capture capability. Either run with `CAP_NET_RAW`/`CAP_NET_ADMIN`
  (`sudo setcap cap_net_raw,cap_net_admin=eip $(readlink -f $(which dotnet))` or the published
  binary), or run as root. Documented in the README Linux section.
- To capture Albion traffic the game must run on (or route through) the same host/network path.

## Roadmap

- [x] Install .NET 10 SDK; confirm cross-platform parse chain builds.
- [ ] **Engine first:** `Core` library with a settings-abstracted libpcap capture provider →
      Photon parser; console host that decodes live/sample traffic. Verified on Linux (Docker).
- [ ] Web dashboard skeleton: device selection, server-detection status, live decoded-event feed.
- [ ] Damage meter (DPS/healing/fame per player).
- [ ] Market & loot (auction search/prices, loot logger, trade monitor).
- [ ] Dungeon & map (dungeon tracker, entry timer, map history).
- [ ] Linux packaging + run docs (Dockerfile, setcap, README).

## De-duplication (later)

Phase 1 keeps the Windows app untouched, so a small amount of capture logic is adapted into
`Core` rather than shared. Once the Linux build is proven, the WPF app can reference `Core` for
the shared capture/parse/tracking code, removing the duplication. Tracked as a follow-up.

## Building

```bash
# cross-platform projects (engine, web) — build/run anywhere
dotnet build src/StatisticsAnalysisTool.Core/StatisticsAnalysisTool.Core.csproj

# Windows-only WPF app (unchanged) — requires Windows
dotnet build src/StatisticsAnalysisTool/StatisticsAnalysisTool.csproj
```

The Windows WPF project (`net10.0-windows`) will not restore/build on Linux — that is expected
and does not affect the cross-platform engine or web dashboard.
