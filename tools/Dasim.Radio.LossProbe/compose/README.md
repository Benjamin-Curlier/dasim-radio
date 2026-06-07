# Emulated impaired-LAN rig

A one-command way to run the loss probe across an **impaired network link** without a test room or a
second machine. It uses Linux `tc netem` (verified present in Docker Desktop's WSL2 kernel) to add
latency, jitter, loss and reordering to the broker's delivery path, then you run the probe against it.

## What it impairs (and the catch)

The `shaper` container shares the `nats` container's network namespace and applies netem to its **egress**
— i.e. the `nats → subscriber` direction, which is the `audio.out` delivery path a real listener depends
on. That's the representative leg for the FEC question.

**The catch — and the whole point:** voice is core NATS over **TCP**. netem drops/reorders at the IP
layer, but TCP retransmits, so the application (the subscriber) still receives **every** frame, just
later. So with a moderate `loss`/`delay` you will see **inter-arrival jitter and latency rise while
app-level frame loss stays ~0**. That is the finding: TCP converts packet loss into jitter, not the
isolated single-frame loss Opus FEC recovers. App-visible frame loss only appears when the link gets bad
enough to break TCP into a **reconnect burst**, or when the consumer can't keep up (slow-consumer drop).

## Prerequisites

- Docker Desktop with Linux containers (WSL2 backend). First run pulls `nats:2.10` + `alpine` and
  installs `iproute2` in the shaper (needs internet once).

## Experiment 1 — loss becomes jitter (the headline)

```bash
# bash
NETEM="delay 20ms 10ms distribution normal loss 5% reorder 25% 50%" docker compose up -d
```
```powershell
# PowerShell
$env:NETEM = "delay 20ms 10ms distribution normal loss 5% reorder 25% 50%"; docker compose up -d
```
```bash
dotnet run -c Release --project .. -- both --url nats://127.0.0.1:4222 --duration 30
docker compose down
```

Expect: `Lost ~0`, but inter-arrival p95/max and one-way latency well above the clean 20 ms. Verdict:
**NEGLIGIBLE LOSS** — because TCP hid it.

## Experiment 2 — reconnect burst (the loss that actually happens)

Start the rig (any NETEM), begin a longer run, then sever the link mid-run:

```bash
docker compose up -d
dotnet run -c Release --project .. -- both --url nats://127.0.0.1:4222 --duration 40
# in another terminal, ~15 s in — black-hole the broker's egress for a few seconds:
docker compose exec shaper tc qdisc change dev eth0 root netem loss 100%
sleep 5
docker compose exec shaper tc qdisc change dev eth0 root netem delay 20ms loss 1%
```

Or, more bluntly, bounce the broker (drops all connections):

```bash
docker compose restart nats
```

Expect: a **disconnect count > 0** and a **single long gap** (one big burst) in the histogram — the
shape Opus FEC cannot recover and only PLC can briefly mask. Verdict: **BURST-DOMINATED**.

## Experiment 3 — slow consumer (no netem needed)

```bash
docker compose up -d
dotnet run -c Release --project .. -- both --url nats://127.0.0.1:4222 \
  --consumer-delay 40 --sub-capacity 16 --sub-fullmode dropnewest --duration 15
docker compose down
```

Expect: substantial loss in short (1–2 frame) bursts — the slow-consumer drop mode (issue #27 territory).

## Tuning netem

`NETEM` is passed verbatim to `tc qdisc … root netem <NETEM>`. Useful forms:

| Goal | NETEM |
|---|---|
| Pure latency | `delay 40ms` |
| Latency + jitter | `delay 40ms 15ms distribution normal` |
| Random loss | `loss 10%` |
| **Correlated/bursty loss (WiFi-like)** | `loss gemodel 5% 50%` (Gilbert–Elliott) |
| Reordering | `delay 10ms reorder 30% 50%` |
| Duplication / corruption | `duplicate 1%` / `corrupt 0.5%` |

Change live without restarting: `docker compose exec shaper tc qdisc change dev eth0 root netem <args>`.

## Caveats

- This emulates the transport; it is **not** the full media-service mix path (no force tree, no
  per-listener encode). The loss characteristic we're deciding on lives in the transport.
- netem here cannot reproduce real WiFi PHY/driver behaviour. The closest cheap *real* test is the
  probe's `pub`/`sub` modes across two physical devices on your actual WiFi. Use this rig to find the
  failure boundary; use real hardware for the final FEC go/no-go.
