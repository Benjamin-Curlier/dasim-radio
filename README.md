# Dasim.Radio

A LAN-based, cross-platform (Linux + Windows) voice radio stack that mirrors a military
chain of command. Push-to-talk respects the hierarchy: a superior pre-empts (cuts off) a
subordinate on the same net, and a member can talk both up and down the tree.

> Status: **`v1.0.0-rc.1` — structurally complete, not yet hardware-verified.** Every host
> exists and the solution is CI-green (0 warnings) on Linux **and** Windows with unit +
> Testcontainers integration tests: `Core`, `Contracts`, `Messaging`, the Opus codec seam
> (Concentus + native libopus), the **media service** (floor authority + per-listener routing,
> mix, and degradation), the **agent** daemon, the **client** engine, and the **manager** core.
> See [docs/architecture.md](docs/architecture.md) and [docs/routing-mix-model.md](docs/routing-mix-model.md).
>
> **Before this becomes `v1.0.0`, two release gates remain** — this is a release candidate, not a
> supported release:
> 1. **Hardware verification.** The UI/device/PTT surface (Avalonia app, OwnAudioSharp device I/O,
>    SharpHook/evdev hotkeys, Blazor manager) is **build-only and unverified on real hardware** —
>    the end-to-end "push a key, be heard" path has not yet been exercised with real audio devices.
>    Validate each PR's manual-test checklist before tagging `v1.0.0`.
> 2. **Transport security ([#11](https://github.com/Benjamin-Curlier/dasim-radio/issues/11)).** NATS
>    currently runs without TLS, authentication, or subject permissions. Until this lands, any host on
>    the LAN can connect and impersonate any participant, so the chain-of-command clearance model is
>    not enforced at the transport. Do not deploy outside a trusted lab network.

## Architecture at a glance

Two clearly separated planes over a single NATS server (`srv_brk:4222`):

- **Control plane** — JetStream / KV / Services (persisted, request/reply): force tree,
  post↔member associations, configurations, presence, floor state, agent commands.
- **Data plane** — core NATS (ephemeral, low latency): Opus voice frames. **Never
  JetStream** (persistence/replay is wrong for real-time voice).

A central **media service** is the authority: it enforces floor control (strict
pre-emption), mixes per listener, and applies **per-listener** quality/clarity
degradation before delivering each client its own stream.

| Concern | Choice |
|---|---|
| Runtime | .NET 10 / C# 14 |
| Client UI | Avalonia (native audio + global PTT hotkeys) |
| Manager UI | Blazor |
| Audio I/O | OwnAudioSharp / PortAudio / miniaudio (cross-platform) |
| Codec | Opus — Concentus (managed) in the client; native libopus in the media service |
| Messaging | NATS (`NATS.Net`) — JetStream/KV/Services + core |
| Tests | xUnit v3 + FakeTimeProvider; Testcontainers (NATS) for integration |

## Documentation

| Guide | For |
|---|---|
| [Architecture](docs/architecture.md) · [Routing & mix model](docs/routing-mix-model.md) · [Tech stack](docs/tech-stack.md) | Design, decision log, library choices |
| [Deployment guide](docs/deployment.md) | Standing up NATS + the four hosts on a LAN |
| [User guide](docs/user-guide.md) | Radio operators (client) and administrators (manager) |
| [Operations guide](docs/operations.md) | Running the deployed system: discovery, control, observability, faults |
| [Developer guide](docs/developer-guide.md) | Contributing: build, conventions, testing, workflow |

## Repository layout

```
src/
  Dasim.Radio.Core              Domain: force tree, floor control, net topology, mix planner
  Dasim.Radio.Contracts         NATS subjects + wire DTOs (primitives only)
  Dasim.Radio.Messaging         NATS.Net wrappers (audio bus, KV store, floor/presence/degrade)
  Dasim.Radio.Audio             Codec + audio I/O abstraction (IOpusEncoder/Decoder)
  Dasim.Radio.Audio.Concentus   Managed-Opus impl (client)
  Dasim.Radio.Audio.Opus        Native-libopus (OpusSharp) impl (media service)
  Dasim.Radio.MediaService      The authority host: floor + per-listener routing/mix/degrade
  Dasim.Radio.Agent             Host daemon: presence heartbeat + agent.<host>.cmd service + process control
  Dasim.Radio.Client            Headless client engine (PTT/floor state machine + transmit/receive pumps)
  Dasim.Radio.Client.Audio.OwnAudio  OwnAudioSharp device I/O (build-only, unverified on hardware)
  Dasim.Radio.Client.Ptt        Native PTT input (SharpHook/evdev) (build-only, unverified on hardware)
  Dasim.Radio.Client.App        Avalonia client app (build-only, unverified on hardware)
  Dasim.Radio.Manager.Core      Manager services (config CRUD, force-tree import/validate, degrade)
  Dasim.Radio.Manager           Blazor manager UI (build-only, unverified on hardware)
tests/                          Core · Integration (Testcontainers) · Audio · Audio.Opus · MediaService · Client · Manager
benchmarks/                     BenchmarkDotNet (Opus encode throughput + mix hot-path)
tools/Dasim.Radio.LossProbe     Data-plane loss/jitter measurement harness (FEC/PLC decision)
docs/                           architecture.md · routing-mix-model.md · tech-stack.md · deployment.md · user-guide.md · operations.md · developer-guide.md · phase2-kickoff.md · wayland-ptt-spike.md
Directory.Build.props           Shared build settings (nullable, analyzers, warnings-as-errors)
Directory.Packages.props        Central Package Management (all NuGet versions)
```

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/) (`dotnet --version` ≥ 10.0)

## Build & test

```bash
dotnet restore Dasim.Radio.slnx
dotnet build   Dasim.Radio.slnx -c Release
dotnet test    Dasim.Radio.slnx -c Release
```

## CI / quality

GitHub Actions ([.github/workflows/ci.yml](.github/workflows/ci.yml)) builds and tests on
Linux **and** Windows, then runs a **SonarCloud** analysis (free for private repos under
50k LoC). Configure these and the scan runs automatically (it is skipped gracefully when the
token is absent):

- secret `SONAR_TOKEN`
- variable `SONAR_ORGANIZATION`
- variable `SONAR_PROJECT_KEY`

An on-prem **self-hosted SonarQube Community** alternative is provided under
[docker/sonarqube](docker/sonarqube/) as a fallback.

## Branching model

GitHub Flow: `main` is always releasable; work happens on short-lived `feature/*` branches
merged via PR. Releases are marked with **tags** (`vX.Y.Z`); pre-releases carrying open
release gates use a suffix (`vX.Y.Z-rc.N`). A `release/X.Y` branch is created only when a
version already deployed in the field must be patched without shipping the latest `main`.
Code, comments, commits and PRs are written in **English**.
