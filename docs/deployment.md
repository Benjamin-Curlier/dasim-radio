# Deployment Guide

How to deploy the Dasim.Radio stack on a LAN. Companion to the
[architecture](architecture.md) and [operations](operations.md) guides.

> **Release-candidate status (`v1.0.0-rc.1`).** Two gates are still open and bound where you may
> deploy:
> 1. The **UI / device / PTT layer** (Avalonia client app, OwnAudioSharp device I/O, SharpHook/evdev
>    hotkeys, Blazor manager) is **build-only and unverified on real hardware**. Run each PR's
>    manual-test checklist on the target machines before relying on it.
> 2. **NATS has no transport security** ([#11](https://github.com/Benjamin-Curlier/dasim-radio/issues/11)):
>    no TLS, no authentication, no subject permissions. Any host on the LAN can connect and impersonate
>    any participant. **Deploy only on a trusted, isolated lab network.**

## Topology

A single NATS server carries everything; the four hosts are NATS clients.

```
                          ┌──────────────────────────┐
                          │  NATS server  srv_brk:4222│  (JetStream enabled)
                          │  control plane + data plane│
                          └──────────────────────────┘
            audio.in/out ▲      ▲ floor.*           ▲ agent.* / presence / KV
                         │      │                   │
        ┌────────────────┴──┐ ┌─┴──────────────┐ ┌──┴───────────────┐
        │  Media service    │ │   Clients (N)  │ │  Agents (1/post) │ ── launch/stop ── Client
        │  (the authority)  │ │  Avalonia app  │ │  daemon/service  │
        └───────────────────┘ └────────────────┘ └──────────────────┘
                                                   ▲
                                          ┌────────┴─────────┐
                                          │  Manager (Blazor)│  authors configs, imports the
                                          └──────────────────┘  force tree, drives agents
```

| Host | Project | Output | Hosting model | Count |
|---|---|---|---|---|
| **Media service** | `src/Dasim.Radio.MediaService` | console exe (Generic Host) | run as a daemon/service | **1** (single instance is enough at ≤50 pax) |
| **Agent** | `src/Dasim.Radio.Agent` | console exe (Generic Host) | systemd / Windows Service | **one per post** |
| **Client** | `src/Dasim.Radio.Client.App` | Avalonia desktop app (`WinExe`) | launched by the agent or by hand | **one per operator** |
| **Manager** | `src/Dasim.Radio.Manager` | ASP.NET Core / Blazor Server | web server (Kestrel) | **1** (admin console) |

## Prerequisites

- **.NET 10 runtime** on every host (or publish self-contained — see below).
- **A NATS server with JetStream** reachable at `srv_brk:4222` (see below). The repo does **not**
  ship the broker or any infra-as-code for it (the only compose files are the SonarQube analysis rig
  and the [LossProbe](operations.md#loss-measurement-lossprobe) netem rig — neither is production infra).
- **Media service only:** native `libopus`. It is supplied automatically by the `OpusSharp.Natives`
  package for `win-x64`, `linux-x64` (and arm64 variants) — no hand-built binary needed, just publish
  for a matching RID.
- **Client only:** PortAudio/miniaudio (bundled via `OwnAudioSharp`) and the PTT backend
  (`SharpHook`/libuiohook on Windows & X11; raw `evdev` on Linux/Wayland). These are the
  unverified-on-hardware pieces.

## 1. Stand up the NATS server

JetStream **must** be enabled (the control plane uses KV buckets and Services). The data plane uses
core NATS on the same server.

```bash
# Native binary
nats-server -js

# or Docker
docker run -p 4222:4222 nats:latest -js
```

Point every host at it with `Nats:Url` (default `nats://srv_brk:4222`) — set the `srv_brk` hostname
in DNS/hosts, or override per host (see [Configuration](#configuration)).

**KV buckets are created automatically** on first access by `IControlPlaneStore`
(`src/Dasim.Radio.Messaging/KeyValue/NatsControlPlaneStore.cs`) — you do not pre-create them:

| Bucket | Purpose | Special config |
|---|---|---|
| `force_tree` | imported hierarchy (key `current`) | keeps **5 revisions** (`BucketConfigs.ForceTreeHistory`) |
| `presence` | agent heartbeats | **15s TTL** (`ControlPlaneTtls.Presence`) |
| `endpoints` | post ↔ host/IP | — |
| `configs` | client launch configurations | — |
| `floor_state` | authoritative floor holder per net | — |

## 2. Publish the hosts

From the repo root (the solution stays at **0 warnings**):

```bash
dotnet build Dasim.Radio.slnx -c Release        # sanity build
dotnet publish src/Dasim.Radio.MediaService -c Release -r linux-x64  -o ./deploy/media
dotnet publish src/Dasim.Radio.Agent        -c Release -r linux-x64  -o ./deploy/agent
dotnet publish src/Dasim.Radio.Manager      -c Release -r linux-x64  -o ./deploy/manager
dotnet publish src/Dasim.Radio.Client.App   -c Release -r win-x64    -o ./deploy/client
```

- Pick the RID per target: `linux-x64`, `win-x64`, `linux-arm64`, `win-arm64`. The **media service
  RID must match** so the right `libopus` native is copied.
- Add `--self-contained` if the target has no .NET 10 runtime installed.
- `Native AOT` is intentionally **not** enabled yet (it would break the 0-warning gate on NATS.Net /
  Hosting trim warnings) — publish framework-dependent or self-contained.

Each publish copies its `appsettings.json`; edit it in the output, or override at runtime.

## 3. Configuration

Standard .NET configuration precedence (later wins): `appsettings.json` →
`appsettings.{Environment}.json` → **environment variables** (`Section__Key`) → command-line. Every
host reads `Nats:Url`.

### Media service — `appsettings.json`

```json
{ "Nats": { "Url": "nats://srv_brk:4222" } }
```

- `Routing:CombinePolicy` — `Override` (default) or `Additive`. Parsed case-insensitively in
  `Program.cs`; absent ⇒ `Override`. Not present in the shipped file — add it or set
  `Routing__CombinePolicy=Additive` to switch a deployment to additive (cocktail-party) mixing.

### Agent — `appsettings.json`

```json
{
  "Nats": { "Url": "nats://srv_brk:4222" },
  "Agent": {
    "HostId": "post-01",
    "HostName": "Post 01",
    "IpAddress": "",
    "HeartbeatInterval": "00:00:05",
    "ClientExecutablePath": "",
    "AllowReplaceRunningClient": false
  }
}
```

- **`Agent:HostId`** is the post's stable identity. It is the `agent.<hostId>.cmd` subject token and
  the `presence` KV key, so it **must be a single NATS token** (no `.`, `*`, `>`, whitespace).
  Validated at startup — a bad value fails the host fast.
- `Agent:HeartbeatInterval` must stay well under the 15s presence TTL (default 5s; validated positive).
- `Agent:ClientExecutablePath` is the client binary the agent launches on `launch`/`reconfigure`.
- `Agent:AllowReplaceRunningClient` — when `false` (default), a `launch` is rejected while a client
  is already running; stop it first.

### Client — `appsettings.json`

```json
{
  "Nats": { "Url": "nats://srv_brk:4222" },
  "Client": {
    "ClientId": "client-01",
    "ParticipantId": "client-01",
    "OwnNetId": "alpha",
    "ParentNetId": null,
    "CaptureDeviceId": null,
    "PlaybackDeviceId": null,
    "Ptt": { "Key": "VcSpace", "EvdevDevice": null, "EvdevKeyCode": null }
  }
}
```

- `Client:ClientId` is the audio identity (`audio.in/out.<clientId>`); `Client:ParticipantId` is the
  **force-tree node id** (the media service derives priority from it — the wire priority is never
  trusted). Both must be single NATS tokens; validated at startup.
- `Client:OwnNetId` / `Client:ParentNetId` are the nets this client transmits/listens on.
- `Client:CaptureDeviceId` / `Client:PlaybackDeviceId` — `null` = system default.
- `Client:Ptt:Key` is the SharpHook key name (Windows/X11). On Linux/Wayland set
  `Client:Ptt:EvdevDevice` (e.g. `/dev/input/event3`) + `Client:Ptt:EvdevKeyCode`.

When an agent launches a client, the launch config comes from a `ClientConfigDto` in the `configs`
bucket (authored in the Manager), **not** from this local file.

### Manager — `appsettings.json`

```json
{
  "Nats": { "Url": "nats://srv_brk:4222" },
  "Manager": { "PresenceStaleAfter": "00:00:15" }
}
```

- `Manager:PresenceStaleAfter` — a post whose last heartbeat is older than this is shown offline
  (default 15s, matching the presence TTL).
- Bind the web endpoint with `ASPNETCORE_URLS` (e.g. `http://0.0.0.0:5000`); front it with a reverse
  proxy for anything beyond a lab.

## 4. Run as a service

### Agent (systemd — Linux)

The agent calls `AddSystemd()` and `AddWindowsService()`; both are no-ops off-service, so one binary
works either way.

```ini
# /etc/systemd/system/dasim-radio-agent.service
[Unit]
Description=Dasim Radio Agent
After=network-online.target

[Service]
Type=notify
ExecStart=/opt/dasim-radio/agent/Dasim.Radio.Agent
Environment=Nats__Url=nats://srv_brk:4222
Environment=Agent__HostId=post-01
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now dasim-radio-agent
```

### Agent (Windows Service)

```powershell
New-Service -Name DasimRadioAgent `
  -BinaryPathName "C:\dasim-radio\agent\Dasim.Radio.Agent.exe" -StartupType Automatic
Start-Service DasimRadioAgent
```

### Media service & Manager

Neither calls the service-integration helpers today, so run them under your process supervisor of
choice (systemd unit as above without `Type=notify`, NSSM, or a container entrypoint).

## 5. Seed the topology

There is no bootstrap script — seeding is done through the Manager (see the
[User Guide](user-guide.md)):

1. Start the **media service**, then the **Manager**.
2. In the Manager, **import the force tree** (paste a `ForceTreeDto` JSON; validated for unique
   single-token ids, non-empty names, a root, and no cycles).
3. **Author client configurations** (one `ClientConfigDto` per operator) on the Configurations page.
4. Start an **agent** per post; it appears under Posts within one heartbeat (~5s).
5. **Launch** a client onto a post by selecting a config — or start `Dasim.Radio.Client.App` by hand
   with its local `appsettings.json`.

## What the repo does *not* provide (yet)

- No production Dockerfile / compose / Helm / Terraform for the hosts or the broker.
- No TLS / NKey / JWT / subject permissions on NATS ([#11](https://github.com/Benjamin-Curlier/dasim-radio/issues/11)).
- No verified hardware path for audio devices or global PTT — treat the client/manager as
  *build-validated only* until you complete on-hardware testing.
