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
- **Codec**: Opus behind the `Dasim.Radio.Audio` seam — **Concentus** (managed) in the client,
  native **libopus** (OpusSharp) in the media service. Both implemented.
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
src/Dasim.Radio.Core            Domain: force tree, floor control, net topology,    (no deps)
                                mix planner + IMixPolicy (the crown jewel)
src/Dasim.Radio.Contracts       NATS subjects + wire DTOs (primitives only)         (no deps)
src/Dasim.Radio.Messaging       NATS.Net wrappers: audio bus, KV store, floor/      (-> Contracts)
                                presence/degrade signalling, agent-command service
src/Dasim.Radio.Audio           Codec + audio I/O abstraction (IOpusEncoder/Decoder, AudioFormat)
src/Dasim.Radio.Audio.Concentus Managed-Opus (Concentus) impl — for the client
src/Dasim.Radio.Audio.Opus      Native-libopus (OpusSharp) impl — for the media service
src/Dasim.Radio.MediaService    The authority host: floor authority + force-tree provider +
                                force-tree priority resolver + per-listener router/mix/degrade
tests/  Core.Tests · Integration.Tests (Testcontainers NATS) · Audio.Tests ·
        Audio.Opus.Tests · MediaService.Tests
benchmarks/Dasim.Radio.Audio.Benchmarks   BenchmarkDotNet (encode throughput)
docs/architecture.md            Design + decision log (source of truth)
docs/routing-mix-model.md       Routing/mix design (subtree nets, policies, transmit)
```

Still to build (Phase 2): `Dasim.Radio.Agent` (daemon), `Dasim.Radio.Client` (Avalonia),
`Dasim.Radio.Manager` (Blazor); device I/O via `OwnAudioSharp`.

## Commands

```bash
dotnet build Dasim.Radio.slnx -c Release      # must stay 0 warnings / 0 errors
dotnet test  Dasim.Radio.slnx -c Release
dotnet add <project> package <id>             # add a dependency (CPM)
```

## Workflow

- GitHub Flow: branch `feature/*` off `main`; PR back into `main` (which stays releasable).
- `main` is protected by a Ruleset: **PRs are required** and CI (Linux + Windows) must pass.
  No direct pushes; self-merge is allowed (0 reviews).
- Tag releases `vX.Y.Z`. Create a `release/X.Y` branch only to patch a version already deployed in the field.
- Every change keeps the build green and tests passing. New domain rules get unit tests first.

## NATS subject scheme (see `Dasim.Radio.Contracts.Subjects`)

- Voice: `audio.in.<clientId>`, `audio.out.<clientId>` (core NATS). **Audio is per-client, not
  per-net** — the media service mixes and decides what each client receives. Clearance ("can't
  hear nets above your rank") is therefore enforced **server-side in the router**, not by per-net
  subjects; NATS subject perms only isolate each client to its own `audio.in/out.<self>` and govern
  `floor.*`. (Refines architecture §5 — see issue #11.)
- Floor: `floor.request`, `floor.release`, `floor.events.<netId>`
- Control: `agent.<hostId>.cmd`, `presence.heartbeat`, `cmd.degrade`
- KV buckets: `force_tree` (key `Subjects.Keys.ForceTreeCurrent` = `"current"`), `endpoints`,
  `configs`, `presence`, `floor_state`

## Routing & mix model (see `docs/routing-mix-model.md`)

- **Subtree nets**: one net per non-leaf force-tree node = `{node} ∪ {direct children}`; net id =
  node id. A leader is on 2 nets (owns one = talk *down*, parent's = talk *up*); a leaf is on 1.
- **Strict floor ⇒ bounded mix**: ≤1 speaker per net, so a listener mixes ≤ (their nets) ≈ 2
  sources — never 50. The real cost is per-listener *encode* fan-out (de-risked by the benchmark).
- **Combine policy is a strategy** (`IMixPolicy`): `PriorityOverride` (default — a superior cuts
  through) or `Additive` (sum concurrent nets). Selected by `Routing:CombinePolicy`.
- **Transmit = net-select** (one net per PTT): default own net, modifier = parent net.
- **Priority is authoritative**: `ForceTreePriorityResolver` derives it from the force-tree node,
  never the client-sent wire value. Unknown participant → lowest priority (and routes to nobody).
- Default (override) gives a **zero-transcode pass-through**; only degraded or multi-source
  (additive) listeners are decoded → mixed → re-encoded.

## Messaging library — usage conventions (consumed by the Phase-2 hosts)

- One NATS connection per host via `AddDasimRadioMessaging(url, configure?)`; it pins
  `RadioSerializerRegistry` (raw bytes for audio, source-gen JSON for control DTOs). **Every new
  wire DTO must be added to `RadioJsonContext`** or it fails loudly at serialize time.
- Reach KV buckets through `IControlPlaneStore` (it binds + caches each bucket); don't recreate
  stores per call. `clientId`/`netId`/`hostId` must be single NATS tokens (no `.`/`*`/`>`).
- NATS Services handlers (`IAgentCommandServer`) receive a **service-lifetime** token (cancelled
  when the handle is disposed) — never pass a request- or startup-scoped token into a handler.

## Status

- **Done**: solution + conventions; `Core` (force tree, floor control, net topology, mix planner);
  `Contracts`; `Messaging` (NATS.Net wrappers + Testcontainers integration); `Audio` seam +
  `Concentus` + `OpusSharp` codecs + encode benchmark; `MediaService` — floor authority +
  force-tree provider/priority resolver + **per-listener routing, mix (override + additive),
  degradation** (Phase-2 issue #7 complete, PRs #18–#22). **Mix hot-path perf hardening**:
  allocation-free `Deliveries`/`Render` (pooled buffers, version-cached floor snapshot), profile-keyed
  encode-sharing, in-place encoder retune (no rebuild glitch), xorshift clarity dither — `MixHotPath`
  benchmark shows ~185 KB/tick → 0. CI (Linux/Windows + SonarCloud ≥80% new-code), GitHub Ruleset + wiki.
  `Agent` (#8, PR #29) — `Dasim.Radio.Agent` daemon: presence heartbeat (core-NATS channel + TTL'd
  `presence` KV) + `agent.<host>.cmd` NATS service (launch/stop/reconfigure) + single-client process
  controller (`IProcessRunner` seam, one `Lock`) + validated `AgentOptions` + systemd/Windows Service
  hosting. Native AOT kept friendly; `PublishAot` deferred (0-warning gate). `Client` (#9) — headless
  engine (PTT/floor state machine + transmit/receive pumps), PTT input core (session detect + evdev
  parse + key-edge), audio conversion/reframing, `CompositePushToTalk` — all **tested**; native PTT
  providers (`SharpHook`/`evdev`), `OwnAudio` device I/O, and the Avalonia app — **build-only**.
  `Manager` (#10) — tested `Manager.Core` services (config CRUD, force-tree import/validate, post
  directory, post control, degrade) + `ClientConfigDto`, behind a **build-only** Blazor UI.
- **✅ v1 structurally complete**: every host exists. The UI/device/PTT layers (Avalonia app,
  OwnAudioSharp, SharpHook/evdev, Blazor) are CI-green but **build-only / UNVERIFIED on real
  hardware** — validate per each PR's manual-test checklist before tagging a release. CI:
  Build&Test (Linux+Windows) are the required gates; integration (Testcontainers) runs Linux-only;
  SonarCloud is advisory.
- **Post-v1 backlog (open issues)**: #11 NATS security (TLS + NKey/JWT + subject perms);
  #24 per-net degrade scoping (currently whole-listener); #27 drop stale audio under data-plane
  saturation; #34 `ForceTreeMapper` null-children guard. See [docs/phase2-kickoff.md](docs/phase2-kickoff.md).

## Subagents (`.claude/agents/`)

- **floor-control-reviewer** — for `Core` floor-control + `Contracts` changes.
- **realtime-audio-reviewer** — for `Audio`/`MediaService` hot-path changes. **Run it on every
  media-service hot-path PR (mix/encode/decode) before merge** — it caught the allocation/
  encode-sharing scaling items on the additive slice.
