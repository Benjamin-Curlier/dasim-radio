# Architecture

> **Status: frozen for v1 (2026-06-04); Phase-2 implementation in progress.** The core design
> below is locked and has held up in implementation. Decisions taken while building Phase 2 are
> appended to the decision log (D8+) and the routing/mix design is detailed in
> [routing-mix-model.md](routing-mix-model.md). Phase-2 library versions are pinned in
> `Directory.Packages.props` after verification.

## 1. Goal

A LAN-based voice radio stack mirroring a military chain of command. Push-to-talk (PTT)
respects the hierarchy: a superior pre-empts a subordinate on the same net; a mid-tree
member can talk both up and down (section leader → group leader / company commander).
Target: ≤ 50 participants, hierarchy groups usually much smaller. Linux + Windows.

## 2. Two planes over one NATS server

A single NATS server (`srv_brk:4222`) carries everything, but in two distinct roles.

### Control plane — JetStream / KV / Services (persisted, request/reply)

| Subject / bucket | Purpose |
|---|---|
| KV `force_tree` | Imported hierarchy (versioned) |
| KV `endpoints` | Post ↔ IP/name associations |
| KV `configs` | Configurations authored by the manager |
| KV `presence` | Agent/client heartbeat (TTL) |
| KV `floor_state` | Authoritative floor holder per net |
| SVC `agent.<host>.cmd` | Launch / stop / reconfigure a post |
| `cmd.degrade` | Per-listener quality/clarity command |

### Data plane — core NATS (ephemeral, low latency)

| Subject | Purpose |
|---|---|
| `audio.in.<clientId>` | Raw Opus frames published by a client |
| `audio.out.<clientId>` | Per-listener mixed + degraded stream from the media service |

**Voice never uses JetStream.** Persistence, acknowledgements and replay add latency and
are useless for real-time voice — a 20 ms frame that arrives late is dropped; Opus PLC/FEC
conceals loss. Budget check: 50 × ~32 kbps ≈ 1.6 Mbps — trivial on a LAN.

## 3. Components

- **Client** (Avalonia, per person): capture → Opus encode → `audio.in`; subscribe to
  `audio.out.<self>` → decode → play. Native audio I/O and **global** PTT hotkeys (work
  when unfocused) drive the framework choice — a webview does not help with either.
- **Media service** (the authority): subscribes to all `audio.in`, enforces floor control,
  mixes **per listener**, applies per-listener degradation, publishes `audio.out.<client>`.
  Single instance is enough at 50 pax; shard by net only if ever needed.
- **Agent** (daemon/service per post): presence heartbeat (manager discovery), handles
  `agent.<host>.cmd` to launch/stop the client with a configuration, reports status.
  Candidate for Native AOT.
- **Manager** (Blazor): authors configurations, imports the force tree, discovers posts,
  launches/stops clients, issues degradation commands. No audio → Blazor fits well.

## 4. Floor control (the core domain)

`Dasim.Radio.Core.FloorControlService` — one floor per net, authoritative, thread-safe.

- A request is **granted** on an idle net.
- A re-request by the current holder is an idempotent grant.
- A **strictly higher** priority request pre-empts the holder → `GrantedWithPreemption`
  (the cut-off participant is reported). This is "the chief cuts off the subordinate".
- An equal or lower priority request is **denied** while the floor is held.
- Release by the holder frees the floor; release by anyone else is ignored.

Priority is an integer (higher wins), derived from the participant's position in the force
tree. The service is owned by the media service so every client observes identical
decisions. `TimeProvider` is injected (no `DateTime.Now`) for deterministic tests.

## 5. Security (military context, even on a LAN)

- NATS **TLS** + decentralized auth (NKeys/JWT, operator/account model).
- Map the hierarchy onto **NATS subject permissions** — authorization at the broker, not only
  in the client.
- **Refinement (Phase 2):** the audio plane turned out to be **per-client**
  (`audio.in/out.<clientId>`), not per-net, so "a member cannot hear nets above their clearance"
  is enforced **server-side in the media-service router** (membership + the force-tree priority
  resolver), not by per-net audio subjects. NATS subject perms isolate each client to its own
  `audio.in/out.<self>` and govern who may publish `floor.request`/`floor.release`. Folded into
  the NATS-security work (issue #11).

## 6. Decision log

| # | Decision | Rationale |
|---|---|---|
| D1 | .NET 10 / C# 14 | Ample for ≤50 pax on LAN; no need for Rust/C++. Revisit only if a future mixer profiles as the bottleneck. |
| D2 | Voice on **core NATS**, not JetStream | JetStream is persistence/replay; wrong layer for real-time voice. |
| D3 | **Central media service** authority | Chosen "per-listener" degradation requires central mix + DSP; also enforces strict pre-emption consistently. |
| D4 | **Strict pre-emption** | Matches "a superior cuts off subordinates"; no queue. |
| D5 | **Avalonia** for the client | Native audio + global PTT hotkeys; webview (Photino) does not solve the hard 80%. Blazor kept for the manager. |
| D6 | Codec split: **Concentus** (client), **native libopus** (media service) | Managed Concentus keeps the Avalonia client free of native deps (simple Linux packaging). The media service does the heavy per-listener encoding, so it uses native libopus for throughput. Both sit behind the `Dasim.Radio.Audio` abstraction. |
| D7 | NATS is the single fabric | No second broker. A media service is a NATS *client*, not a replacement. "Scalable audio service" is premature at 50 pax. |
| D8 | **Subtree nets** (one net per non-leaf node = node + direct children; net id = node id) | Resolves "derive nets from the force tree". A leader sits on its own net (talk down) + its parent's (talk up); drops onto the per-net floor unchanged. Detail: [routing-mix-model.md](routing-mix-model.md). |
| D9 | **Combine policy is a strategy** (`IMixPolicy`: `PriorityOverride` default, `Additive`) | Membership + floor are identical; only the final combine differs. Selectable per deployment (`Routing:CombinePolicy`). Strict floor ⇒ a listener mixes ≤ (their nets) ≈ 2 sources, so the bottleneck is encode fan-out, not mixing. |
| D10 | **Transmit = net-select** (one net per PTT; default own net, modifier parent net) | Matches a real radio channel selector; already expressible in `FloorRequestMessage`. |
| D11 | **Priority from the force tree, never the wire** (`ForceTreePriorityResolver`) | Closes a rank-spoofing gap: a client cannot inflate its priority to pre-empt a superior. Unknown participant → lowest priority. |

## 7. Open items for Phase 2

Resolved:
- ~~`Dasim.Radio.Messaging`, `Dasim.Radio.Audio`, `Dasim.Radio.Integration.Tests`~~ — done.
- ~~Derive nets and per-participant priority from the force tree~~ — D8/D11; see
  [routing-mix-model.md](routing-mix-model.md).

Still open:
- Per-listener **encode sharing** (group listeners by net-set + degradation profile) — deferred to
  a *measured* perf pass (BenchmarkDotNet first); strict floor keeps real fan-out low, so measure
  before optimising.
- Remaining hosts: `Dasim.Radio.Agent`, `Dasim.Radio.Client` (Avalonia), `Dasim.Radio.Manager`
  (Blazor); device I/O (`OwnAudioSharp`).
- NATS security hardening (TLS + NKey/JWT + the per-client subject-permission model above) — issue #11.
