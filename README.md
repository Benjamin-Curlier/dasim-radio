# Dasim.Radio

A LAN-based, cross-platform (Linux + Windows) voice radio stack that mirrors a military
chain of command. Push-to-talk respects the hierarchy: a superior pre-empts (cuts off) a
subordinate on the same net, and a member can talk both up and down the tree.

> Status: **Phase 1 — foundation**. Core domain (floor control + force tree), shared
> contracts, CI and project conventions are in place. Hosts (client, agent, manager,
> media service) come in Phase 2. See [docs/architecture.md](docs/architecture.md).

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

## Repository layout

```
src/
  Dasim.Radio.Core        Domain: force tree + floor-control state machine (the crown jewel)
  Dasim.Radio.Contracts   NATS subjects + wire DTOs (primitives only)
tests/
  Dasim.Radio.Core.Tests  Unit tests for the domain
docs/architecture.md      Architecture + decision log
Directory.Build.props     Shared build settings (nullable, analyzers, warnings-as-errors)
Directory.Packages.props  Central Package Management (all NuGet versions)
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
merged via PR. Releases are marked with **tags** (`vX.Y.Z`). A `release/X.Y` branch is
created only when a version already deployed in the field must be patched without shipping
the latest `main`. Code, comments, commits and PRs are written in **English**.
