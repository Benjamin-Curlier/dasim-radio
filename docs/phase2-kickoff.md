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
- **Deferred (open issues):** measured perf pass (per-frame allocations + encode-sharing + encoder
  retune-via-CTL — BenchmarkDotNet first); per-net degrade scoping (currently whole-listener).
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

> **Next up: item 4 (Agent), then 5 (Client) / 6 (Manager).** Items 1–3 are ✅ done (see "Where we
> are"). Before the client (item 5), spike the **Wayland PTT** risk (issue #12).

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
     net-set + degradation profile) is deferred to a *measured* perf pass (BenchmarkDotNet first).

4. **`feature/agent` → `Dasim.Radio.Agent`** — daemon: presence heartbeat (discovery) +
   `agent.<host>.cmd` (launch/stop/reconfigure) via NATS Services. Consider Native AOT.

5. **`feature/client` → `Dasim.Radio.Client`** — Avalonia: device selection + **global PTT via
   SharpHook**.
   - ⚠️ **Spike the Wayland/X11 risk FIRST** (libuiohook is X11-only; unfocused hotkey fails on
     Wayland). See the dedicated spike issue. Decide: require Xorg, detect `XDG_SESSION_TYPE`,
     or accept focused-only on Wayland.

6. **`feature/manager` → `Dasim.Radio.Manager`** — Blazor: config CRUD, force-tree import from
   NATS KV, post discovery, launch/stop, degrade commands.

## Open risks to keep validating

- **Wayland breaks the global PTT** (item 5) — spike before building the client UI.
- **Native libopus glibc baseline** — if ever hand-building, match the deployment baseline.
- **Per-listener encode cost** — measure (item 3); fall back to encode-sharing / complexity 5–7.

## Cross-session continuity

Canonical, always-loaded context is `CLAUDE.md`. Architecture/decisions in
`docs/architecture.md`, stack in `docs/tech-stack.md`. Domain reviewers live in
`.claude/agents/`. Recommended local setup (not yet applied): run `/fewer-permission-prompts`
and add a `dotnet format` Stop hook + a `bin/obj` write-guard via `/update-config`.
