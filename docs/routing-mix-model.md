# Routing & mix model

> Companion to the frozen [architecture](architecture.md). Resolves its Phase-2 open items
> "derive nets and per-participant priority from the force tree" and "per-listener encode sharing".
> Implemented across PRs #19–#22 (Core routing model → routing spine + force-tree priority →
> degradation → additive mix), then hardened in the mix hot-path perf pass (allocation-free routing,
> profile encode-sharing, in-place encoder retune, xorshift clarity).

## 1. What is a net?

**Subtree nets.** One net per non-leaf node of the force tree; its members are that node plus its
**direct children**. The net id *is* the owning node's id, so it lines up 1:1 with the floor's
`NetId`.

```
CO ── Alpha ── A1 ── A1a ── p1, p2
                  └─ A1b ── p3
nets: CO{CO,Alpha} · Alpha{Alpha,A1} · A1{A1,A1a,A1b} · A1a{A1a,p1,p2} · A1b{A1b,p3}
```

A leader sits on **two** nets — the one it owns (talk *down*) and its parent's (talk *up*); a leaf
member and the root sit on one. This is the model behind "talk up and down the tree".

> Rejected alternative: *ego-centric chain sets* (no standing nets; each PTT addresses a computed
> audience). It would have redesigned the per-net floor (the crown jewel) and dissolved the stable
> subjects, for little gain at ≤50 participants.

## 2. The key insight: the mix is bounded

Strict floor control guarantees **at most one speaker per net**. A listener therefore mixes at most
*(number of nets they belong to)* sources — **≤2 in a clean tree**, not 50. The feared "50-way mix"
never happens. The real cost is the per-listener **encode** fan-out, de-risked by the
BenchmarkDotNet PoC (~50 encodes / 20 ms is comfortable).

## 3. Listening — combine policy (`IMixPolicy`, in Core)

When a listener has two active nets at once, the two sources are combined per a pluggable strategy
(`Routing:CombinePolicy`, default `Override`):

- **`PriorityOverride`** (default) — the listener hears only the highest-priority active net; a
  superior *cuts through* and suppresses lower chatter, extending "a chief cuts off a subordinate"
  across echelons. Single clean stream; lower audio is dropped while the superior talks.
- **`Additive`** — the listener hears every active net they are on, summed. Nothing is lost
  (cocktail-party); two talkers at once can be harder to follow.

Membership + floor are identical for both; only the final combine differs.

## 4. Transmitting — net-select

One net per PTT: default the net the participant **owns** (talk down), with a modifier to talk on
the **parent** net (talk up). Already expressible in `FloorRequestMessage(NetId, …)`; the client
maps its two PTT modes to `NetMembership.OwnedNet` / `ParentNet`.

## 5. Priority is authoritative

`ForceTreePriorityResolver` derives a request's priority from the participant's force-tree node —
**never** the client-sent wire value (which a client could inflate to pre-empt a superior). An
unknown participant resolves to the lowest possible priority and, being on no net, routes to nobody.
This replaced the interim client-trusting `RequestPriorityResolver`.

## 6. The media-service pipeline

1. `ForceTreeProvider` watches the `force_tree` KV bucket (key `"current"`), rebuilds the
   `NetTopology` + `MixPlanner` on each version, and rejects an invalid tree without unseating the
   one in use.
2. `MediaRouterService` consumes `audio.in.>`. For each frame from speaker *S*:
   - `MixRenderer.Remember(S, frame)` buffers it (and marks its PCM stale).
   - `MediaRouter.Deliveries(S)` returns the listeners whose **trigger** — the highest-priority
     source in their plan (`MixSources.Highest`) — is *S*, each with the full source set to combine.
     This emits a listener's mix **exactly once per cycle** (so a multi-source additive listener
     isn't double-emitted) and stays event-driven — **no tick/clock**.
   - `MixRenderer.Render(deliveries)`:
     - a single undegraded source → **pass-through** the original Opus bytes (zero transcode);
     - otherwise decode each source (lazily, **at most once per cycle** via the stale flag — so a
       speaker summed into N listeners decodes once), sum + clamp, apply clarity DSP if degraded,
       then **encode once per shared `(source-set, quality, clarity)` profile** and fan the resulting
       bytes to every listener with that profile, publishing to each `audio.out.<listener>`.

The hot path is **allocation-free in steady state** (verified by the `MixHotPath` BenchmarkDotNet
scenario): `Deliveries` reuses pooled delivery/source buffers and `MixPlanner.PlanInto`
(`IMixPolicy.SelectInPlace`) avoids per-frame lists; `FloorControlHolders` caches its holder snapshot
and rebuilds only when `FloorControlService.Version` moves (not every 20 ms tick); `Render` reuses its
output list and per-group encode buffers (no `ToArray`); and all hot loops index instead of `foreach`
(an `IReadOnlyList<T>` `foreach` boxes its enumerator). Per-speaker decoders and **per-profile
encoders** are cached (Opus is stateful); a profile whose quality changes **retunes its encoder in
place** (`IOpusEncoder.Retune` → `OPUS_SET_BITRATE`/`OPUS_SET_COMPLEXITY`) rather than rebuilding it,
so the stream stays continuous (no click); idle profile encoders are evicted after a grace window. The
renderer is single-threaded (one consumer) — the returned frames alias reused buffers, so the consumer
publishes each cycle before the next `Render`.

## 7. Degradation (`cmd.degrade`)

`DegradeCommandService` applies commands into a `DegradeRegistry` (per-listener `DegradeProfile`; a
clean 100/100 profile restores pass-through). **Quality** (0–100) maps to encoder bitrate (6 k–24 k)
+ complexity (`QualityEncoderSettings`); **clarity** (0–100) maps to a PCM one-pole low-pass +
additive static (`ClarityProcessor`, a seeded xorshift dither — ~⅓ the cost of `Random` + `Math.Round`
per 960-sample frame; the seed is injectable for deterministic tests). Any degradation forces the
transcode path for that listener.

## 8. Known v1 simplifications / deferred

- **Per-net degrade scoping** — `DegradeCommand.NetId` is accepted but applied whole-listener.
- **DTX assumption** — the additive trigger relies on the holder streaming continuously (client DTX
  off), so a non-trigger source contributes as a buffered peer; a stale/absent peer is treated as
  silence.
- **Data-plane saturation** — the publish loop awaits NATS.Net per listener; `PublishAsync` queues
  into the connection's write pipe (no per-call flush), and back-pressure is per-*connection*, so a
  fronting channel cannot relieve it. Under sustained saturation the right fix is **dropping stale
  audio**, not more buffering — not yet implemented (see `MediaRouterService`).

### Done — measured perf pass

Per-frame allocation, encode-sharing, encoder retune, and the clarity DSP were hardened in a measured
pass (`MixHotPath` + `Clarity` BenchmarkDotNet scenarios first): the steady-state mix tick went from
~185 KB/tick to **0 managed allocation**. See §6, §7, and `IOpusEncoder.Retune`. Re-reviewed clean by
the realtime-audio-reviewer and floor-control-reviewer agents.
