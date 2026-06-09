# Running on Linux

Upstream **AlbionOnline-StatisticsAnalysis** is a Windows-only WPF app. This fork adds a
**cross-platform engine + web dashboard** that run natively on Linux (and macOS/Windows). The
original Windows WPF app is unchanged; the Linux build is a separate, additive set of projects.

See [PORTING.md](../PORTING.md) for the architecture and roadmap.

## What works today

- **Capture + Photon/Protocol18 parsing** on Linux via `libpcap` (the same parser the Windows app
  uses) — `StatisticsAnalysisTool.Core`.
- **`sat-cli`** — a headless tool: verify the parser/aggregation, list capture devices, run a live decode.
- **Web dashboard** (`StatisticsAnalysisTool.Web`) — a browser UI with:
  - capture status, detected server, totals, live decoded-event feed, top event codes;
  - **Damage meter** — live damage / DPS / healing per player;
  - **Loot log** — who looted which item / how much silver, from whom;
  - **Map history** — zones entered, with time spent in each.

Each dashboard panel has a **Replay** button that injects crafted sample packets, so you can see it
work without live game traffic.

**Not yet ported / known limits:** loot shows item **ids** (the item-name database isn't ported
yet) and map history shows raw **cluster ids / island names** (the friendly world-zone database
isn't ported yet). Auction-house market data, dungeon timers, and the richer per-spell combat
breakdowns from the Windows app are future milestones. The engine already decodes the traffic these
build on — see [PORTING.md](../PORTING.md).

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

## Option A — Docker (recommended)

```bash
docker compose up --build        # then open http://localhost:8087
```

Open the dashboard at **<http://localhost:8087>** — not `http://0.0.0.0:8087` (`0.0.0.0` is the
bind-all address the server listens on, not a browsable URL).

The default compose publishes the port, so it's reachable on Windows, macOS and Linux. Equivalent
plain Docker:

```bash
docker build -t sat-web .
docker run --rm -p 8087:8087 --cap-add NET_RAW --cap-add NET_ADMIN sat-web
```

> **Live capture needs host networking (native Linux only).** To capture real game traffic the
> container must share the host network — on a native Linux host, swap the published port for host
> networking: in `docker-compose.yml` remove the `ports:` block and uncomment `network_mode: host`
> (or `docker run --network host …`). Host networking is **not** supported on Docker Desktop
> (Windows/macOS); there the dashboard works via the published port but cannot see game traffic, so
> run natively on the Linux box where Albion runs.

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

| Symptom | Fix |
|---------|-----|
| `Native libpcap could not be loaded` / `Unable to load DLL 'pcap'` | Install libpcap and ensure `libpcap.so` exists (see Requirements). |
| `devices` lists nothing, or capture won't start | Missing `CAP_NET_RAW` — run with `sudo` or `setcap`. |
| Dashboard works but no traffic is decoded | Albion isn't running on this host, traffic is on an unexpected interface, or a VPN is in use — try `--all` (CLI) or the “capture all” toggle (web). |
| `selftest` passes but live capture is empty | The parser is fine; it's a capture/privilege/interface issue, not a parsing one. |
