# Dasim.Radio — Development Instructions

LAN voice-radio stack (Linux + Windows) that mirrors a military chain of command.
Push-to-talk respects the hierarchy (a superior pre-empts a subordinate; members talk up
and down the tree). Read [docs/architecture.md](docs/architecture.md) for the full design
and decision log before making structural changes.

## Architecture (must respect)

This is a **multi-host system with a shared kernel** — NOT Clean/VSA (those target a single
API). Shared libraries + thin deployable hosts.

- **Two planes over one NATS server (`srv_brk:4222`):**
  - Control plane = JetStream / KV / Services (persisted, request/reply).
  - Data plane = **core NATS** for Opus voice. **Never put voice on JetStream.**
- **Central media service is the authority**: enforces floor control, mixes **per
  listener**, applies **per-listener** quality/clarity degradation.
- **Floor control = strict pre-emption** (a higher priority cuts off the holder; equal or
  lower is denied). Lives in `Dasim.Radio.Core.FloorControlService`; it is the crown jewel
  and must stay heavily unit-tested.

## Tech stack

- **Runtime**: .NET 10 / C# 14
- **Client UI**: Avalonia (native audio + global PTT hotkeys)
- **Manager UI**: Blazor (no audio)
- **Audio I/O**: OwnAudioSharp / PortAudio / miniaudio (cross-platform). Never NAudio (Windows-only).
- **Codec**: Opus via **Concentus** (managed). Keep it behind the `Dasim.Radio.Audio`
  abstraction so the media service can swap to native **libopus** if it saturates.
- **Messaging**: `NATS.Net` (JetStream/KV/Services + core)
- **Tests**: xUnit v3, FakeTimeProvider; Testcontainers (NATS) for integration

## Conventions

- Language: **English** for all code, comments, commits and PRs.
- File-scoped namespaces; `_camelCase` private fields; `sealed` by default; nullable enabled.
- **No `DateTime.Now`** — inject `TimeProvider` (already done in floor control).
- Strongly-typed ids/values (`ParticipantId`, `NetId`, `Priority`) — do not pass raw strings/ints across the domain.
- `Dasim.Radio.Contracts` uses **primitives only** (wire DTOs) — never reference `Core` from it.
- Central Package Management: add packages with `dotnet add <proj> package <id>` (versions land in `Directory.Packages.props`); never hardcode versions in csproj.
- Warnings are errors (`Directory.Build.props`). Keep the build at 0 warnings.

## Repository layout

```
src/Dasim.Radio.Core        Domain: force tree + floor control       (no dependencies)
src/Dasim.Radio.Contracts   NATS subjects + wire DTOs (primitives)   (no dependencies)
tests/Dasim.Radio.Core.Tests
docs/architecture.md        Design + decision log (source of truth)
```

Planned (Phase 2): `Dasim.Radio.Messaging`, `Dasim.Radio.Audio`, `Dasim.Radio.MediaService`,
`Dasim.Radio.Agent`, `Dasim.Radio.Client`, `Dasim.Radio.Manager`, `Dasim.Radio.Integration.Tests`.

## Commands

```bash
dotnet build Dasim.Radio.slnx -c Release      # must stay 0 warnings / 0 errors
dotnet test  Dasim.Radio.slnx -c Release
dotnet add <project> package <id>             # add a dependency (CPM)
```

## Workflow

- GitHub Flow: branch `feature/*` off `main`; PR back into `main` (which stays releasable).
- Tag releases `vX.Y.Z`. Create a `release/X.Y` branch only to patch a version already deployed in the field.
- Every change keeps the build green and tests passing. New domain rules get unit tests first.

## NATS subject scheme (see `Dasim.Radio.Contracts.Subjects`)

- Voice: `audio.in.<clientId>`, `audio.out.<clientId>` (core NATS)
- Floor: `floor.request`, `floor.release`, `floor.events.<netId>`
- Control: `agent.<hostId>.cmd`, `presence.heartbeat`, `cmd.degrade`
- KV buckets: `force_tree`, `endpoints`, `configs`, `presence`, `floor_state`

## Status

- **Done**: solution + conventions, `Core` (force tree + floor control), `Contracts`, unit
  tests (13 passing), CI (Linux/Windows + Sonar), GitHub Flow.
- **Next**: messaging + audio abstractions, then the four hosts (Phase 2).
