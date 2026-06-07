# Developer Guide

Onboarding for contributors. The deep design lives in [architecture.md](architecture.md) (frozen
design + decision log) and [routing-mix-model.md](routing-mix-model.md); the canonical agent/dev
rules are in [`CLAUDE.md`](../CLAUDE.md). This guide is the practical "how to build, test, and ship."

> **Release-candidate status (`v1.0.0-rc.1`).** Every host exists and CI is green, but the
> UI/device/PTT layer is unverified on hardware and NATS has no transport security
> ([#11](https://github.com/Benjamin-Curlier/dasim-radio/issues/11)). See the
> [README](../README.md) for the two release gates.

## Architecture in one paragraph

A **multi-host system with a shared kernel** — *not* Clean/VSA (those target a single API). Thin
deployable hosts sit on shared libraries. One NATS server carries two planes: a **control plane**
(JetStream/KV/Services) and a **data plane** (core NATS for Opus voice — never JetStream). The
**media service is the authority**: it enforces strict-pre-emption floor control, mixes **per
listener**, and applies **per-listener** degradation. The crown jewel is
`Dasim.Radio.Core.FloorControlService` (+ the mix planner / `IMixPolicy`) — keep it heavily tested.

## Repository layout

```
src/
  Dasim.Radio.Core              Domain: force tree, floor control, net topology, mix planner (no deps)
  Dasim.Radio.Contracts         NATS subjects + wire DTOs — primitives only, never references Core
  Dasim.Radio.Messaging         NATS.Net wrappers (audio bus, KV store, floor/presence/degrade, agent svc) → Contracts
  Dasim.Radio.Audio             Codec + audio I/O seam (IOpusEncoder/Decoder, AudioFormat)
  Dasim.Radio.Audio.Concentus   Managed-Opus impl — for the client
  Dasim.Radio.Audio.Opus        Native-libopus (OpusSharp) impl — for the media service
  Dasim.Radio.MediaService      The authority host: floor + per-listener routing/mix/degrade
  Dasim.Radio.Agent             Host daemon: presence + agent.<host>.cmd + process control
  Dasim.Radio.Client            Headless client engine (PTT/floor state machine + pumps) — tested
  Dasim.Radio.Client.Audio.OwnAudio   OwnAudioSharp device I/O — build-only
  Dasim.Radio.Client.Ptt        Native PTT (SharpHook/evdev) — build-only
  Dasim.Radio.Client.App        Avalonia client app — build-only
  Dasim.Radio.Manager.Core      Manager services (config CRUD, force-tree import, degrade) — tested
  Dasim.Radio.Manager           Blazor manager UI — build-only
tests/    Core · Integration (Testcontainers NATS) · Audio · Audio.Opus · MediaService · Client · Manager
benchmarks/Dasim.Radio.Audio.Benchmarks    BenchmarkDotNet (encode throughput + MixHotPath)
tools/Dasim.Radio.LossProbe                 Data-plane loss/jitter harness (FEC/PLC decision)
docs/                                       architecture · routing-mix-model · tech-stack · deployment · user-guide · operations
```

**Dependency rule:** `Core` and `Contracts` have no project dependencies. `Contracts` uses
**primitives only** and must **never** reference `Core`. Everything wire-facing goes through
`Contracts`; everything domain goes through `Core`.

## Prerequisites & first build

- **.NET SDK 10** (`dotnet --version` ≥ 10).
- `gh` for PRs. Docker (Linux containers) only for the Testcontainers integration tests.

```bash
dotnet restore Dasim.Radio.slnx
dotnet build   Dasim.Radio.slnx -c Release    # MUST stay 0 warnings (warnings-as-errors)
dotnet test    Dasim.Radio.slnx -c Release
```

The solution file is the **`.slnx`** format (`Dasim.Radio.slnx`).

## Conventions (enforced)

- **English** everywhere — code, comments, commits, PRs.
- File-scoped namespaces; `_camelCase` private fields; `sealed` by default; **nullable enabled**.
- **No `DateTime.Now`** — inject `TimeProvider` (floor control already does; tests use
  `FakeTimeProvider`).
- **Strongly-typed ids/values** (`ParticipantId`, `NetId`, `Priority`) across the domain — don't pass
  raw strings/ints.
- **Warnings are errors** (`Directory.Build.props`). The build and CI both gate on 0 warnings, and CI
  runs `dotnet format --verify-no-changes` — run `dotnet format` before pushing.
- **Central Package Management.** Add packages with `dotnet add <project> package <id>` so the version
  lands in `Directory.Packages.props`. **Never hand-write a version** in a csproj.

## Messaging library — usage rules

- One NATS connection per host via `AddDasimRadioMessaging(url, configure?)`. It pins
  `RadioSerializerRegistry` (raw bytes for audio, source-gen JSON for control DTOs).
- **Every new wire DTO must be added to `RadioJsonContext`** or it fails loudly at serialize time.
- Reach KV buckets through `IControlPlaneStore` (it binds + caches each bucket) — don't recreate
  stores per call.
- `clientId` / `netId` / `hostId` must be **single NATS tokens** (no `.` / `*` / `>`).
- NATS Services handlers (`IAgentCommandServer`) receive a **service-lifetime** token (cancelled when
  the handle is disposed) — never pass a request- or startup-scoped token into a handler.

## Testing

- **xUnit v3**, `FakeTimeProvider` for time, **Testcontainers** (NATS) for integration.
- New **domain rules get unit tests first** — especially floor control and routing/mix.
- Integration tests (`tests/Dasim.Radio.Integration.Tests`) spin a real NATS JetStream container;
  they run **Linux-only** in CI.
- Build-only UI/device/PTT projects are **excluded from coverage** (they can't run headless) and ship
  with a manual-test checklist instead — see [the build-only policy](#build-only-layers).

## CI & quality gates

GitHub Actions (`.github/workflows/ci.yml`):

- **Required:** `Build & Test (ubuntu-latest)` + `Build & Test (windows-latest)` — 0 warnings, tests
  green, `dotnet format` clean.
- **Integration** (Testcontainers) runs Linux-only.
- **SonarCloud** is advisory but expects **≥80% coverage on new code**; exclude generated/`*.g.cs` and
  the build-only UI projects from coverage.

## Build-only layers

The Avalonia app, OwnAudioSharp device I/O, SharpHook/evdev PTT, and the Blazor UI are **build-only**:
CI-green and compiled, but **unverified on real hardware/display**. All the *logic* they wrap lives in
**tested** libraries (`Client` engine, the PTT input core, `Manager.Core`). Treat the thin host
shells as unproven until you complete the manual-test checklist on the target hardware. See
[deployment.md](deployment.md) and [tech-stack.md](tech-stack.md) for the integration risks (Wayland
PTT, PipeWire latency, OwnAudioSharp full-duplex).

## Workflow (GitHub Flow)

`main` is protected by a Ruleset: **PRs required**, CI (Linux + Windows) must pass, no direct pushes,
no force-push/deletion. **0 reviews required — you may self-merge once CI is green.**

```bash
git switch -c feature/<task> origin/main
# ...work; keep the build at 0 warnings, add tests for new domain rules...
dotnet build Dasim.Radio.slnx -c Release
dotnet test  Dasim.Radio.slnx -c Release
dotnet format Dasim.Radio.slnx                # CI gates on this
git push -u origin feature/<task>
gh pr create --fill --base main
# after CI is green:
gh pr merge --squash --delete-branch
```

Branch `feature/*` off `main`; `main` stays releasable. Tag releases `vX.Y.Z` (pre-releases use a
`-rc.N` suffix while release gates are open). Create a `release/X.Y` branch only to patch a version
already deployed in the field.

## Domain reviewers (`.claude/agents/`)

Two specialist review agents — use them on the matching changes:

- **floor-control-reviewer** — for `Core` floor-control and `Contracts` (wire-contract) changes:
  pre-emption invariants, concurrency, wire compatibility.
- **realtime-audio-reviewer** — for `Audio` / `MediaService` hot-path changes (capture/encode/mix/
  decode/playback). **Run it on every media-service hot-path PR before merge** — it caught the
  allocation / encode-sharing scaling issues on the additive slice.

## Where to start reading

1. [architecture.md](architecture.md) — the frozen design and the D1–D11 decision log.
2. [routing-mix-model.md](routing-mix-model.md) — subtree nets, `IMixPolicy`, transmit = net-select,
   the allocation-free hot path.
3. `Dasim.Radio.Core` — `FloorControl`, `NetTopology`, `Routing` (the crown jewel).
4. `Dasim.Radio.Contracts/Subjects.cs` + `Messages.cs` — the wire surface every host shares.
