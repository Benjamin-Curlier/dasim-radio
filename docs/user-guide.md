# User Guide

For the two human-facing roles: the **radio operator** (the Client app) and the **administrator**
(the Manager). Companion to the [deployment](deployment.md) and [operations](operations.md) guides.

> **Release-candidate status (`v1.0.0-rc.1`).** The tested logic lives in the headless client engine
> and the manager core services. The **Avalonia client window and the Blazor manager pages are
> build-only and unverified on real hardware** — screens may differ from this guide once validated on
> devices. There is no transport security yet; use only on a trusted lab LAN.

---

## Concepts you need first

### The force tree and nets

The system mirrors a chain of command. The administrator imports a **force tree** — a hierarchy of
nodes (e.g. *Commander → Company → Section → Member*). From it the media service derives **nets**
(radio channels):

- One **net per non-leaf node** = that node **plus its direct children**; the net id is the node id.
- A **leader sits on two nets**: the one they own (talk **down** to subordinates) and their parent's
  (talk **up** to their superior).
- A **leaf** (no subordinates) sits on **one net**: their parent's.

```
        Commander                 Nets:
         │                          commander  = {Commander, Alpha-Lead}
      Alpha-Lead                    alpha_lead = {Alpha-Lead, A1, A2}
       │      │                     alpha1     = {A1, p1, p2}
      A1     A2                     alpha2     = {A2, p3}
     /  \     │
    p1  p2   p3            Alpha-Lead is on  commander + alpha_lead.
                           p1 is on alpha1 only.
```

See [routing-mix-model.md](routing-mix-model.md) for the full model.

### Floor control (who gets to talk)

There is **one floor per net**, and it is strict — a real radio channel, not a conference call:

| Situation | Result |
|---|---|
| Net is idle, you press PTT | **Granted** — you are on air. |
| You out-rank the current speaker | **Pre-emption** — you cut them off and take the floor. |
| Someone equal or higher is speaking | **Denied** — you must wait; there is no queue. |
| A superior presses while you speak | You are **pre-empted** — your transmission stops immediately. |

**Priority comes from your position in the force tree**, resolved server-side. You cannot inflate your
rank from the client.

---

## For the radio operator (Client app)

### Starting up

The client is launched either **remotely by an administrator** (via the post's agent) or **by hand**
by running `Dasim.Radio.Client.App` with its `appsettings.json`. On start it connects to NATS,
subscribes to your mixed audio stream and your nets' floor events, and arms push-to-talk.

The main window shows:

- **Net** — the net you transmit on.
- **Floor state** — your current PTT phase and, per net, who currently holds the floor
  (e.g. `alpha_lead: Alpha-Lead   alpha1: idle`).
- An **● ON AIR** indicator while you are transmitting.
- **Microphone** and **Speaker** device pickers.
- A large **HOLD TO TALK** button (on-screen fallback for PTT).

### Push-to-talk

Hold your PTT key (or the on-screen button) to talk; release to stop. Any configured input works —
the global hotkey and the on-screen button are reference-counted, so the floor is held as long as
*any* of them is pressed and released when the **last** one lets go.

- **Windows / X11:** a global hotkey via SharpHook (works even when the app is not focused),
  configured by `Client:Ptt:Key` (e.g. `VcSpace`).
- **Linux / Wayland:** a raw `evdev` device read, configured by `Client:Ptt:EvdevDevice` +
  `Client:Ptt:EvdevKeyCode` (SharpHook's global hook is X11-only).

### What you experience in each PTT phase

| Phase | Meaning |
|---|---|
| **Idle** | Not transmitting. |
| **Requesting** | You pressed PTT; the request is in flight to the authority (a sub-frame moment on a LAN). |
| **Transmitting** | You hold the floor — your mic is live on the net (**● ON AIR**). |
| **Denied** | The net is held by someone of equal or higher rank — you are not on air. Keep holding and you will be granted when the net frees, or release and try later. |
| **Pre-empted** | A superior cut in; your transmission stopped. |

> **Transmit target.** Today the client transmits on its **own net**. The "modifier key to talk up
> to the parent net" is part of the design (transmit = net-select) but is a follow-up, not in this rc.

### What you hear

The media service mixes a stream **just for you** from the nets you are a member of. Because the floor
allows at most one speaker per net, you hear **at most ~2 sources at once** — never a crowd. Two
combine policies (chosen per deployment):

- **Override** (default): if two of your nets are active, you hear the **higher-priority** one only.
- **Additive**: you hear them **summed** together.

An administrator can **degrade** your reception (lower quality and/or clarity) to simulate a poor
radio link — see below.

---

## For the administrator (Manager app)

The Manager is a Blazor web console. It does not handle audio. Open it in a browser at its configured
URL. Three pages:

### Posts (home, `/`)

Live directory of posts, discovered from agent heartbeats.

- Each row shows the host name/id, **online/stale** status, and the currently running client.
- **Launch** a client: pick a configuration and start it on that post's agent.
- **Stop** the running client.
- **Degrade** a listener: set **Quality (0–100)** and **Clarity (0–100)** for a target client and
  apply; reset to 100/100 to restore. *(In this rc, degrade applies to the listener's whole mix —
  per-net scoping is [#24](https://github.com/Benjamin-Curlier/dasim-radio/issues/24).)*
- **Refresh** the list.

### Force tree (`/force-tree`)

Import and view the hierarchy.

- Paste a **`ForceTreeDto`** JSON and import. The import is **revision-checked** (optimistic
  concurrency): if someone else imported since you loaded the page, your write is rejected rather than
  clobbering theirs.
- Validation rejects: a missing root, duplicate ids, ids that are not single NATS tokens, and empty
  names.

```jsonc
{
  "version": 1,
  "root": {
    "id": "commander", "name": "Commander", "kind": "Command", "priority": 100,
    "children": [
      { "id": "alpha_lead", "name": "Alpha Leader", "kind": "Company", "priority": 80,
        "children": [
          { "id": "a1", "name": "Alpha 1", "kind": "Section", "priority": 60,
            "children": [
              { "id": "p1", "name": "Member P1", "kind": "Member", "priority": 40, "children": [] }
            ] }
        ] }
    ]
  }
}
```

- `kind` is the echelon label (free text). `priority` is the floor authority (higher wins).
  `children` may be `[]` for a leaf.

### Configurations (`/configs`)

Author the per-operator launch configs an agent uses to start a client. Each is a **`ClientConfigDto`**:

| Field | Meaning |
|---|---|
| `configId` | Unique id for this config (single NATS token). |
| `clientId` | The client's audio identity (`audio.in/out.<clientId>`). |
| `participantId` | The **force-tree node id** — drives the operator's authoritative priority. |
| `ownNetId` | The net this operator transmits/listens on. |
| `parentNetId` | Optional parent net (for a leader who is on two nets). |
| `displayName` | Human-readable label. |
| `hostId` | Optional agent host to associate. |

> Note: the launch config deliberately **omits priority** (the force tree is authoritative) and
> device/codec preferences (those are local, per-machine settings in the client's own
> `appsettings.json`).

---

## Quick start (operator's-eye view)

1. An admin imports the force tree and creates your configuration.
2. Your client is launched on your post (or you start it yourself).
3. The main window shows your net and floor state.
4. Hold PTT → **Requesting** → **● ON AIR**. Speak. Release to free the net.
5. If you are **Denied**, someone of equal/higher rank holds the net — wait or retry.
6. If a superior cuts in, you are **Pre-empted** and your transmission stops.
