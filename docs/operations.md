# Operations Guide

Running and operating the deployed Dasim.Radio stack day-to-day (FR: *exploitation*) — discovery,
control, observability, faults, and measurement. Companion to the [deployment](deployment.md) and
[user](user-guide.md) guides.

> **Release-candidate status (`v1.0.0-rc.1`).** No transport security yet
> ([#11](https://github.com/Benjamin-Curlier/dasim-radio/issues/11)) — operate on a trusted,
> isolated LAN. The client/manager UIs are unverified on hardware.

## The two planes (mental model)

| Plane | Transport | Carries |
|---|---|---|
| **Control** | JetStream / KV / Services (persisted, request/reply) | force tree, configs, endpoints, presence, floor state, agent commands |
| **Data** | core NATS (ephemeral, low latency) | Opus voice frames (`audio.in/out.<clientId>`) |

Voice is **never** on JetStream. A late frame is useless and is dropped.

## Subjects & buckets reference

All names come from `Dasim.Radio.Contracts.Subjects`.

**Subjects**

| Subject | Plane | Purpose |
|---|---|---|
| `audio.in.<clientId>` | core | client's captured Opus frames |
| `audio.out.<clientId>` | core | per-listener mixed/degraded stream |
| `audio.in.>` | core | wildcard the media service subscribes to |
| `floor.request` / `floor.release` | core | PTT press / release |
| `floor.events.<netId>` | core | floor decisions per net |
| `agent.<hostId>.cmd` | Service | launch / stop / reconfigure a post's client |
| `presence.heartbeat` | core | agent heartbeats |
| `cmd.degrade` | core | per-listener quality/clarity command |

**KV buckets**

| Bucket | Key | Value | Notes |
|---|---|---|---|
| `force_tree` | `current` | `ForceTreeDto` | keeps 5 revisions |
| `endpoints` | `<postId>` | `EndpointDto` | post ↔ host/IP |
| `configs` | `<configId>` | `ClientConfigDto` | client launch configs |
| `presence` | `<hostId>` | `PresenceHeartbeat` | **15s TTL** |
| `floor_state` | `<netId>` | `FloorStateDto` | authoritative holder per net |

You can inspect any of these live with the `nats` CLI, e.g.:

```bash
nats kv ls                          # buckets
nats kv get force_tree current      # the active hierarchy
nats kv ls presence                 # who has heartbeated recently
nats kv get floor_state alpha_lead  # current holder of a net
nats sub 'floor.events.>'           # watch floor decisions live
nats sub presence.heartbeat         # watch posts check in
```

## Discovery / presence

- Each **agent** publishes a `PresenceHeartbeat` every `Agent:HeartbeatInterval` (default **5s**) to
  `presence.heartbeat` **and** writes it to the `presence` KV key `<hostId>`.
- The KV entry has a **15s TTL**, so a crashed post disappears ~15s after its last beat. On a clean
  stop the agent **deletes its key immediately**, so the Manager shows it offline at once.
- `PresenceHeartbeat` fields: `HostId`, `HostName`, `IpAddress`, `ClientId` (currently the launched
  `configId` until the client reports its real audio id), `TimestampUtc`.
- The Manager treats a post as stale when its last beat is older than `Manager:PresenceStaleAfter`
  (default 15s).

**Check who's online:** the Manager **Posts** page, or `nats kv ls presence` /
`nats sub presence.heartbeat`.

## Controlling posts (`agent.<hostId>.cmd`)

A NATS **Service** (request/reply). Request body is an `AgentCommandEnvelope(Kind, ConfigId?)`; reply
is `AgentCommandResult(Accepted, Error?)`. Verbs (`AgentCommandKinds`):

| Verb | Body | Effect |
|---|---|---|
| `launch` | `ConfigId` | start the client with that config |
| `stop` | — | stop the running client |
| `reconfigure` | `ConfigId` | restart the client with a new config |

Normally you drive these from the Manager Posts page. Behaviour notes:

- A `launch` while a client is already running is **rejected** unless
  `Agent:AllowReplaceRunningClient=true` — `stop` first.
- The target host is encoded in the subject, so only the verb travels in the body.
- Handlers run on a **service-lifetime** cancellation token (cancelled on agent shutdown), so an
  in-flight command completes gracefully.

## Degrading a listener (`cmd.degrade`)

Publish a `DegradeCommand(TargetClientId, NetId?, QualityPercent, ClarityPercent)` to `cmd.degrade`
(the Manager Posts page does this for you):

- **QualityPercent (0–100)** drives the Opus re-encode (bitrate + complexity). 100 = original.
- **ClarityPercent (0–100)** drives PCM DSP (band-limit + xorshift dither). 100 = original.
- Restore with `100/100`.
- **`NetId` is accepted but not yet honoured** — degrade currently applies to the listener's whole
  mix. Per-net scoping is [#24](https://github.com/Benjamin-Curlier/dasim-radio/issues/24).

The media service applies the active profile on the next render tick; at 100/100 a single-source
listener is a zero-transcode pass-through.

## Observing the floor

- **Live:** subscribe to `floor.events.<netId>` (or `floor.events.>`). Each `FloorEventMessage`
  carries `NetId`, `Outcome`, `Requester`, `Preempted?`, `CurrentHolder?`.
  - `Outcome` ∈ `granted`, `granted_preemption`, `denied`, `released` (`FloorOutcomes`).
  - On a pre-emption, `Preempted` names the participant who was cut off.
  - `CurrentHolder` lets a late joiner render state from the event alone.
- **Snapshot:** read `floor_state` KV key `<netId>` → `FloorStateDto(NetId, HolderParticipantId?,
  HolderPriority?, HeldSinceUtc?)`. A null holder means idle.

## Faults & resilience (what to expect)

| Behaviour | Detail |
|---|---|
| **Capture/playback device fault** | The client logs the fault and **reopens the device** after a ~1s backoff, retrying rather than dying ([#57](https://github.com/Benjamin-Curlier/dasim-radio/issues/57)/[#58](https://github.com/Benjamin-Curlier/dasim-radio/issues/58)). |
| **Bad encoder settings** | Validated at client **startup** — the host fails fast instead of silently going quiet mid-stream. |
| **Decoder/encoder leak under churn** | The mix renderer **evicts idle sources/streams** after ~250 idle render cycles, so reconnecting clients don't leak native handles ([#49](https://github.com/Benjamin-Curlier/dasim-radio/issues/49)). |
| **Slow consumer on the data plane** | Voice is core NATS over TCP; an overloaded listener causes back-pressure and **bursty drops**. Deliberate stale-audio dropping under sustained saturation is [#27](https://github.com/Benjamin-Curlier/dasim-radio/issues/27). |
| **Floor grant across reconnect** | The client awaits floor-event subscription readiness before arming PTT; a residual resubscribe-gap edge case is [#51](https://github.com/Benjamin-Curlier/dasim-radio/issues/51). |

## Logging

The hosts use the **default .NET console logger** (`Microsoft.Extensions.Logging` via the Generic /
Web host) — log levels are set in each `appsettings.json` `Logging` section. **Serilog is planned but
not yet wired** (it appears in [tech-stack.md](tech-stack.md) as a choice, not in the host code). When
running under systemd, logs go to the journal (`journalctl -u dasim-radio-agent`); under a process
supervisor, capture stdout.

Notable structured log points: presence heartbeat start/failures and graceful deregister (agent);
degrade-applied per listener and floor decisions (media service).

## Loss measurement (LossProbe)

`tools/Dasim.Radio.LossProbe` measures **data-plane loss and its shape** to decide whether Opus
FEC/PLC is worth enabling. Because voice runs over TCP (core NATS), isolated single-frame loss (which
FEC recovers) barely happens; the real loss mode is **bursts** (slow consumer, reconnect) — which FEC
can't fix. The probe quantifies this so the decision is evidence-based. See its
[README](../tools/Dasim.Radio.LossProbe/README.md).

Modes:

```bash
# Self-contained: spins a NATS container, pub+sub in-process (baseline ≈ 0 loss on a healthy box)
dotnet run -c Release --project tools/Dasim.Radio.LossProbe -- local --duration 20

# Across two machines: start the subscriber first (core NATS has no replay), then publish
dotnet run -c Release --project tools/Dasim.Radio.LossProbe -- sub --url nats://10.0.0.5:4222
dotnet run -c Release --project tools/Dasim.Radio.LossProbe -- pub --url nats://10.0.0.5:4222 --duration 60

# Reproduce a bursty slow-consumer drop locally
dotnet run -c Release --project tools/Dasim.Radio.LossProbe -- local \
  --consumer-delay 40 --sub-capacity 16 --sub-fullmode dropnewest --duration 20
```

It reports loss rate, isolated-loss fraction, a gap-length histogram, and jitter, then prints a
verdict (negligible / isolated-dominated / burst-dominated). The current lean: **sequence number for
observability (+PLC), defer FEC** until real-LAN numbers justify it.

## Routine operator tasks

- **Add a post:** deploy + start an agent with a unique `Agent:HostId`; it appears under Posts within
  one heartbeat.
- **Bring an operator on air:** ensure the force tree is imported and a config exists, then `launch`
  the config onto their post.
- **Change the hierarchy:** import a new `ForceTreeDto` in the Manager (revision-checked); the media
  service picks up the new tree from the `force_tree` KV.
- **Switch mixing mode:** set `Routing:CombinePolicy` (`Override`/`Additive`) on the media service and
  restart it.
- **Simulate a bad link:** degrade the listener from the Posts page; reset to 100/100 to restore.
