# Phase 2 — kickoff & handoff

> Read this first when starting Phase 2 in a fresh session. It assumes the frozen
> [architecture](architecture.md) and the [technology stack](tech-stack.md).

## Where we are (updated 2026-06-06)

- **Repo:** `Benjamin-Curlier/dasim-radio` (private). `main` is protected by a **Ruleset**:
  PRs required, CI checks `Build & Test (ubuntu-latest)` + `(windows-latest)` + **SonarCloud
  (≥80% coverage on new code)** must pass, no force-push/deletion. **No direct pushes to `main` —
  everything via feature branch + PR** (0 reviews required, so you can self-merge once CI is green).
- **Done (Phase 1):** `Dasim.Radio.Core` (force tree + strict-preemption floor control),
  `Dasim.Radio.Contracts` (subjects + DTOs). CI + SonarCloud green. Dependabot active.
  Wiki + issues + labels in place.
- **Done (Phase 2 so far):**
  - **Messaging** (#5) — NATS.Net wrappers (audio bus, KV store, floor/presence/degrade signals,
    agent-command service) + Testcontainers integration tests.
  - **Audio** (#6) — codec seam (`IOpusEncoder/Decoder`, `AudioFormat`) + `Concentus` (client) and
    `OpusSharp`/native-libopus (media service) impls + BenchmarkDotNet encode benchmark.
  - **MediaService** (#7, PRs #18–#22) — floor authority host; force-tree provider; **force-tree
    priority resolver** (closes the rank-spoofing gap); **per-listener routing + mix (override +
    additive) + degradation**. See [routing-mix-model.md](routing-mix-model.md). **Issue #7 done.**
  - **Mix hot-path perf pass** — measured (`MixHotPath` + `Clarity` benchmarks first), then made the
    mix tick allocation-free (pooled router/renderer scratch, version-cached floor snapshot,
    index-not-`foreach`), added **profile encode-sharing**, **in-place encoder retune**
    (`IOpusEncoder.Retune`), and an **xorshift clarity** dither. ~185 KB/tick → **0**. Re-reviewed
    clean by both domain agents.
  - **Agent** (#8, PR #29) — `Dasim.Radio.Agent` daemon: presence heartbeat (core-NATS channel +
    TTL'd `presence` KV, immediate beat, graceful deregister) + `agent.<host>.cmd` NATS service
    (launch/stop/reconfigure, never-throw dispatch) + single-child process controller (one `Lock`,
    `IProcessRunner` seam) + validated `AgentOptions` + systemd/Windows Service hosting. No `Contracts`
    change. Native AOT kept friendly (source-gen JSON) but `PublishAot` deferred (0-warning gate).
- **Deferred (open issues):** per-net degrade scoping (#24, currently whole-listener); drop stale audio
  under sustained data-plane saturation (#27 — the publish loop relies on NATS.Net write-pipe back-pressure).
- **Tooling facts:** .NET SDK 10.0.201; `gh` installed at `C:\Program Files\GitHub CLI`
  (prepend to `$env:PATH` inside tool shells if not already resolved). NATS broker is
  `srv_brk:4222`. Solution is `Dasim.Radio.slnx` (.NET 10 `.slnx` format).

## Non-negotiables

- **Versions:** never hand-write package versions. Use `dotnet add <proj> package <id>` so
  Central Package Management records the real latest in `Directory.Packages.props`. Then
  `dotnet build`/`dotnet test` must stay **0 warnings** (warnings-as-errors).
- **Codec:** client = managed Concentus; media service = native libopus (OpusSharp); both
  behind a `Dasim.Radio.Audio` abstraction (`IOpusEncoder`/`IOpusDecoder`).
- **Voice = core NATS, never JetStream.** Control plane = JetStream/KV/Services.

## Start-of-session checklist

```powershell
$env:PATH = "C:\Program Files\GitHub CLI;$env:PATH"   # if gh not resolved
git fetch origin
git switch -c feature/<task> origin/main
# ...work...
dotnet build Dasim.Radio.slnx -c Release   # 0 warnings
dotnet test  Dasim.Radio.slnx -c Release
git push -u origin feature/<task>
gh pr create --fill --base main
# after CI green:
gh pr merge --squash --delete-branch
```

## Work order (each = one feature branch + PR)

Issues are tracked on GitHub under the **`phase:2`** label:
<https://github.com/Benjamin-Curlier/dasim-radio/issues?q=is%3Aissue+label%3Aphase%3A2>

> **✅ v1 is structurally complete — all work-order items (1–6) and the #12 spike are done.** Every
> host exists: Messaging, Audio+codecs, MediaService, Agent, the full Client (engine + PTT + OwnAudio +
> Avalonia app), and the Manager (core + Blazor UI). **The UI/device/PTT layers (Avalonia app,
> OwnAudioSharp, SharpHook/evdev, Blazor UI) are build-only and CI-green but UNVERIFIED on real
> hardware/display — validate them per each PR's manual-test checklist before tagging a release.**
> Post-v1 backlog (open issues): #11 NATS security, #24 per-net degrade scoping, #27 drop-stale-audio,
> #34 ForceTreeMapper null-children guard.

1. ✅ **DONE — `Dasim.Radio.Messaging`** + `tests/Dasim.Radio.Integration.Tests`
   - `dotnet add` : `NATS.Net` (core/JetStream/KV/Services). Tests: `Testcontainers`.
   - Wrap: core publish/subscribe for audio subjects; KV for `force_tree`/`configs`/`presence`/
     `floor_state`; Services for `agent.<host>.cmd`. Use the builders in `Contracts.Subjects`.
   - Integration test: spin a NATS JetStream container, round-trip a KV value and a Service call.
   - Review with the **floor-control-reviewer** agent (contract/wire compatibility).

2. ✅ **DONE — `Dasim.Radio.Audio`**
   - `IOpusEncoder`/`IOpusDecoder`/factories + the capture/playback abstraction.
   - Client impl: `Concentus`. (Device enumeration via `OwnAudioSharp` still TODO — see item 5.)

3. ✅ **DONE — `Dasim.Radio.MediaService`** (issue #7, PRs #18–#22)
   - Native libopus via `OpusSharp` + `OpusSharp.Natives` (ships win-x64 & linux-x64); benchmark in
     `benchmarks/` confirmed ~50 encodes / 20 ms is comfortable.
   - `FloorControlService` wired as the authority; force-tree provider + priority resolver;
     per-listener routing + mix (override + additive) + degradation — see
     [routing-mix-model.md](routing-mix-model.md).
   - Reviewed with the **realtime-audio-reviewer** agent. **Encode-sharing** (group listeners by
     `(source-set, quality, clarity)` profile) + the measured per-frame perf pass are **done** — see
     [routing-mix-model.md](routing-mix-model.md) §6 and the `MixHotPath` benchmark.

4. ✅ **DONE — `Dasim.Radio.Agent`** (issue #8, PR #29) — daemon: presence heartbeat (core-NATS
   channel + TTL'd `presence` KV) + `agent.<host>.cmd` (launch/stop/reconfigure) via NATS Services +
   single-client process controller behind an `IProcessRunner` seam + systemd/Windows Service hosting.
   Native AOT kept friendly (source-gen JSON) but `PublishAot` deferred to keep the 0-warning gate
   (NATS.Net/Hosting trim warnings). `PresenceHeartbeat.ClientId` carries the launched `configId` as a
   placeholder until the client reports its real audio id. Reviewed with the **dotnet code-reviewer**
   agent (the floor/audio domain reviewers don't apply — no `Contracts`/hot-path change).

5. ✅ **DONE — `Dasim.Radio.Client`** (issue #9, PRs #32/#36/#37/#38/#40/#41/#42) — built in slices:
   the headless engine (PTT/floor state machine + transmit/receive pumps, tested), the PTT input core
   (session detect + evdev parse + key-edge, tested), the **build-only** native providers
   (`SharpHookPushToTalk` Win/X11 + `EvdevPushToTalk` Linux), the audio conversion/reframing core
   (tested) + **build-only** `OwnAudio` device adapter, `CompositePushToTalk` (tested), and the
   **build-only** Avalonia app composing it all. PTT per the #12 decision; Avalonia's own `HotKey` is
   focused-only so PTT lives outside the toolkit. GlobalShortcuts portal is a post-v1 fast-follow.

6. ✅ **DONE — `Dasim.Radio.Manager`** (issue #10, PRs #33/#43) — the tested `Manager.Core` services
   (config CRUD, force-tree import + validation, post directory/presence, post control, degrade) +
   `ClientConfigDto`, behind a **build-only** Blazor Server UI (Posts/Configurations/Force-tree pages).

## Open risks to keep validating

- **Wayland breaks the global PTT** (item 5) — ✅ spiked & decided: **evdev** on Linux (works on X11
  + Wayland), SharpHook on Windows, on-screen fallback; portal post-v1. See
  [wayland-ptt-spike.md](wayland-ptt-spike.md).
- **Native libopus glibc baseline** — if ever hand-building, match the deployment baseline.
- **Per-listener encode cost** — measured (perf pass); profile encode-sharing + complexity-5 cap
  landed. Validate again on the real deployment CPU with live (continuously-varying) voice.

## Cross-session continuity

Canonical, always-loaded context is `CLAUDE.md`. Architecture/decisions in
`docs/architecture.md`, stack in `docs/tech-stack.md`. Domain reviewers live in
`.claude/agents/`. Recommended local setup (not yet applied): run `/fewer-permission-prompts`
and add a `dotnet format` Stop hook + a `bin/obj` write-guard via `/update-config`.
