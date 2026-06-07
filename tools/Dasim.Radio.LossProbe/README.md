# Dasim.Radio loss probe

A measurement harness for the audio **data plane** (`audio.out`). It answers one question before we
spend effort on Opus loss concealment:

> Is packet loss on this transport the kind Opus FEC can fix?

## Why this exists

The client receive loop has no per-frame sequence number, so it cannot detect a lost frame and cannot
drive Opus FEC (`DecodeFec`) or PLC (`DecodeLost`) — it just decodes whatever arrives
([`RadioClientEngine`](../../src/Dasim.Radio.Client/RadioClientEngine.cs)). Adding a sequence number is
cheap; adding **FEC** is not — it needs FEC enabled on the encoders (permanent bitrate cost) **and a
≥1‑frame (20 ms) jitter buffer** on the receive path (latency cost, against the low‑latency LAN goal).

The catch: voice runs over **core NATS, which is TCP**. TCP retransmits, so isolated single‑frame loss
— the exact thing in‑band FEC recovers — barely happens in steady state. The losses that *do* happen
are **bursts**: NATS slow‑consumer drops, reconnect gaps (core NATS has no replay), and deliberate
stale‑audio dropping under saturation (issue #27). Opus FEC cannot recover a burst; only PLC can paper
over its first 1–2 frames.

So the deciding number is **not** the loss rate — it's the **isolated‑loss fraction**: what share of
lost frames are single‑frame gaps. This tool measures it, plus the gap‑length histogram, reorder/dup
counts, inter‑arrival jitter, and (in local mode) one‑way latency, and prints a verdict.

## What it measures (and what it doesn't)

- It probes the transport directly — publish to a subject, subscribe, detect gaps by sequence number.
  This is representative of `audio.out` framing, **not** the full media‑service mix path (no force tree,
  no per‑listener encode). That's deliberate: the loss characteristic we're deciding on lives in the
  transport, not the mixer.
- One‑way latency is only valid when the publisher and subscriber **share a clock** (local mode, or both
  roles on one host). Across two machines the clocks differ, so only loss/jitter (clock‑free) are shown.

## Prerequisites

- .NET 10 SDK.
- For `local` mode only: a reachable Docker daemon with **Linux containers** (Docker Desktop default).
  If Docker is awkward, run your own server (`docker run -p 4222:4222 nats:2.10`) and use `pub`/`sub`.

## Quick start — local baseline

```bash
dotnet run -c Release --project tools/Dasim.Radio.LossProbe -- local --duration 20
```

This starts a throwaway `nats:2.10` container, streams 50 fps of 60‑byte frames through it for 20 s, and
prints the report. On a healthy box this should show ~0 loss — the wired‑LAN baseline.

## The real test — two machines

Run the **subscriber first** (core NATS has no replay, so a late subscriber misses early frames — those
show as a join gap, not loss):

```bash
# on the listener machine
dotnet run -c Release --project tools/Dasim.Radio.LossProbe -- sub --url nats://<server-ip>:4222

# on the speaker machine
dotnet run -c Release --project tools/Dasim.Radio.LossProbe -- pub --url nats://<server-ip>:4222 --duration 60
```

Point both at whatever NATS server the radio uses (`srv_brk:4222`). Run it on the realistic worst case:
wired, then WiFi, then while the network is loaded. To capture a **reconnect** burst, pull the cable or
bounce the server mid‑run — the report counts disconnects and the resulting gap.

## Reproducing the bursty failure mode

To show that the loss this system actually suffers is bursty (and therefore FEC‑proof), induce a
slow consumer:

```bash
dotnet run -c Release --project tools/Dasim.Radio.LossProbe -- local \
  --consumer-delay 40 --sub-capacity 16 --sub-fullmode dropnewest --duration 20
```

A subscriber that takes 40 ms per 20 ms frame, with a 16‑deep pending channel that drops the newest
when full, produces exactly the slow‑consumer drop pattern — watch the gap histogram fill with runs > 1.

## Interpreting the output

```
  Lost              : 4  (4%)
  Gaps (bursts)     : 4
  Isolated losses   : 4  (100% of lost)
  Mean / max gap    : 1 / 1 frames
  Gap-length histogram (frames : count):
         1 : 4
  ...
  VERDICT: ISOLATED-DOMINATED LOSS …
```

The verdict applies this rule:

| Measurement | Verdict | Action |
|---|---|---|
| Loss < 0.05% | **Negligible** | Sequence number for observability only; no FEC, no PLC. |
| Loss meaningful **and** ≥50% of lost frames isolated | **Isolated‑dominated** | FEC *could* help — weigh the +20 ms jitter buffer + bitrate cost. |
| Loss meaningful **and** mostly bursts | **Burst‑dominated** | Sequence number + PLC + observability; **defer FEC** (it can't help). |

Add `--json <path>` to also dump the raw numbers for the record.

## Options

Run with no arguments (or `--help`) for the full flag list and defaults.
