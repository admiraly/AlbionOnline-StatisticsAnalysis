# Running on Linux

Upstream **AlbionOnline-StatisticsAnalysis** is a Windows-only WPF app. This fork adds a
**cross-platform engine + web dashboard** that run natively on Linux (and macOS/Windows). The
original Windows WPF app is unchanged; the Linux build is a separate, additive set of projects.

See [PORTING.md](../PORTING.md) for the architecture and roadmap.

## What works today

- **Capture + Photon/Protocol18 parsing** on Linux via `libpcap` (the same parser the Windows app
  uses) — `StatisticsAnalysisTool.Core`.
- **`sat-cli`** — a headless tool: verify the parser/aggregation, list capture devices, run a live decode.
- **Web dashboard** (`StatisticsAnalysisTool.Web`) — a tabbed browser UI:
  - **Overview** — capture status, detected server, totals, live decoded-event feed, top event codes;
  - **Damage meter** — live damage / DPS / healing per player;
  - **Loot log** — who looted which item / how much silver, with **item names + icons**;
  - **Gathering** — gathered resources, per-resource totals with names + icons;
  - **Player** — session fame / silver / might / favor (and per-hour);
  - **Party** — current party members;
  - **Map** — zones entered, with time spent in each;
  - **Item search** — search the bundled item database (names + icons) with **live market prices**
    (AO Data Project API).

Item names/icons come from a bundled Albion item index (`Core/Data/items.txt`, ao-bin-dumps) plus
the official render service; market prices are fetched live. Each panel has a **Replay** button that
injects crafted sample packets, so you can see it work without live game traffic.

**Not yet ported / known limits:** map history shows raw **cluster ids / island names** (the
friendly world-zone database isn't ported yet); mob names aren't resolved. Auction-house *capture*
(vs. the price API), dungeon timers, crafting calculator, guild/storage windows and richer per-spell
combat breakdowns are future milestones. The engine already decodes much of the traffic these build
on — see [PORTING.md](../PORTING.md).

## Requirements

- **.NET 10 SDK** (to build) or **.NET 10 ASP.NET runtime** (to run a published build) — or just
  **Docker**.
- **libpcap**, and an unversioned `libpcap.so`. The capture binding loads `libpcap.so`, which only
  the `-dev` package ships; the runtime package installs a versioned file (`libpcap.so.0.8`). Make
  one available:

  | Distro | Install | Ensure `libpcap.so` |
  |--------|---------|---------------------|
  | Debian/Ubuntu | `sudo apt-get install libpcap0.8` | `sudo apt-get install libpcap-dev` **or** `sudo ln -sf libpcap.so.0.8 /usr/lib/$(uname -m)-linux-gnu/libpcap.so` |
  | Fedora | `sudo dnf install libpcap` | `sudo dnf install libpcap-devel` |
  | Arch | `sudo pacman -S libpcap` | already provides `libpcap.so` |

- **Raw-capture privileges** (`CAP_NET_RAW`, usually `CAP_NET_ADMIN` too) — see below.
- To capture Albion traffic, **the game must run on (or route through) the same host**.

## Option A — Docker on Linux (recommended)

On the **Linux machine where you play Albion**:

```bash
docker compose up --build        # then open http://localhost:8087
```

The default compose uses **`network_mode: host`** (required — the container must share the host's
network to see game traffic) and **auto-starts capture**, so it works out of the box. Open the
dashboard at **<http://localhost:8087>** — not `http://0.0.0.0:8087` (`0.0.0.0` is just the bind-all
address). Equivalent plain Docker:

```bash
docker build -t sat-web .
docker run --rm --network host --cap-add NET_RAW --cap-add NET_ADMIN sat-web
```

The status badge should show **capturing**. Play for a moment and the panels fill in. If a panel
stays empty, see **Troubleshooting** below.

> **Docker Desktop (Windows/macOS) is view-only.** It does not give containers the host's real
> network, so capture cannot work there and `network_mode: host` won't expose the port. To merely
> look at the UI on Windows/macOS, edit `docker-compose.yml`: comment out `network_mode: host` and
> add a `ports: ["8087:8087"]` block (the file has both, with notes). Real capturing must run on the
> Linux box where Albion runs.

## Option B — Native .NET (no Docker)

```bash
# build the cross-platform solution (engine + cli + web; no WPF)
dotnet build src/sat-linux.slnx -c Release

# run the web dashboard
dotnet run --project src/StatisticsAnalysisTool.Web -c Release
# open http://localhost:8087
```

Or publish a self-contained binary (no .NET install needed to run):

```bash
dotnet publish src/StatisticsAnalysisTool.Web -c Release -r linux-x64 --self-contained -o ./out
./out/StatisticsAnalysisTool.Web
```

### Granting capture privileges without root

Capturing raw packets needs `CAP_NET_RAW`. Either run with `sudo`, or grant the capability once to
the executable that runs the capture (the .NET host for a framework-dependent build, or the
published apphost for a self-contained build):

```bash
# self-contained apphost:
sudo setcap cap_net_raw,cap_net_admin=eip ./out/StatisticsAnalysisTool.Web
# framework-dependent (the dotnet muxer runs it):
sudo setcap cap_net_raw,cap_net_admin=eip "$(readlink -f "$(which dotnet)")"
```

> Granting caps to the shared `dotnet` host affects every .NET app it runs. Prefer a self-contained
> publish and `setcap` on its own apphost.

## The headless CLI (`sat-cli`)

Useful for servers, scripting, or just confirming capture works:

```bash
dotnet run --project src/StatisticsAnalysisTool.Cli -- <command>

# or from a self-contained publish:
dotnet publish src/StatisticsAnalysisTool.Cli -c Release -r linux-x64 --self-contained -o ./cli
./cli/sat-cli <command>
```

| Command | What it does |
|---------|--------------|
| `selftest` | Replays a known Photon packet through the parser and verifies the decode. No capture / root needed — a quick "does the engine work here?" check. |
| `combat-selftest` | Crafts NewCharacter + HealthUpdate packets and verifies the damage-meter aggregation. No capture / root needed. |
| `devices`  | Lists capture-capable network devices (proves libpcap is wired up). |
| `capture`  | Starts live capture and decodes Photon traffic. Options: `--seconds N`, `--filter "<BPF>"`, `--all`. |

```bash
./cli/sat-cli selftest
sudo ./cli/sat-cli devices
sudo ./cli/sat-cli capture --seconds 30
```

## Notes & security

- The dashboard binds `http://0.0.0.0:8087` by default (so it works in containers). To keep it
  local-only, set `ASPNETCORE_URLS=http://127.0.0.1:8087`.
- There is **no authentication** on the dashboard — don't expose port 8087 to untrusted networks.
- This tool only **monitors** traffic; it does not modify the game client (same stance as upstream).

## Troubleshooting

### Dashboard opens but every panel is empty

This is almost always a **capture** problem (not a parsing one — the "Replay" buttons working proves
the parser is fine). Check, in order:

1. **Networking.** With Docker you MUST use `network_mode: host` (the default compose) — a bridge /
   published-port container cannot see the host's game traffic. If you changed it to `ports:`, switch
   back to `network_mode: host`. (And this only works on native Linux Docker, not Docker Desktop.)
2. **Is capture running?** The status badge should read **capturing**. If it says *stopped* with a
   red banner, the banner says why (missing libpcap or `CAP_NET_RAW`). Capture auto-starts unless
   `SAT_AUTOSTART=false`.
3. **Privileges.** Capture needs `CAP_NET_RAW` — the compose adds it; bare `docker run` needs
   `--cap-add NET_RAW --cap-add NET_ADMIN`; native runs need `sudo`/`setcap`.
4. **Prove capture headlessly** with the CLI on the same box while the game runs:
   `sat-cli capture --seconds 30` (add `--all` to drop the Photon-port filter, e.g. behind a VPN).
   Zero events means traffic isn't reaching the capture — wrong interface, VPN, or Albion not on this
   host. Non-zero means the engine works and the issue is elsewhere.
5. **Interface / VPN.** If traffic is on an unexpected port (VPN, ExitLag), use the **capture all**
   toggle in the web UI or `--all` on the CLI.

### Other

| Symptom | Fix |
|---------|-----|
| `Native libpcap could not be loaded` / `Unable to load DLL 'pcap'` | Install libpcap and ensure `libpcap.so` exists (see Requirements). |
| `devices` lists nothing, or capture won't start | Missing `CAP_NET_RAW` — run with `sudo` or `setcap`. |
| `selftest` passes but live capture is empty | The parser is fine; it's a capture/privilege/interface/networking issue (see above). |
