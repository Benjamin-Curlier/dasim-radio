# Security (transport hardening — [#11](https://github.com/Benjamin-Curlier/dasim-radio/issues/11))

The design and rollout for NATS **transport security**: authentication, per-client subject
permissions, and TLS. This is the larger of the two `v1.0.0` release gates (the other is hardware
verification of the build-only UI/device/PTT layers).

## Threat model — why this matters

Application logic already enforces the chain of command: the floor authority derives priority from the
authoritative `force_tree` (never the wire) and **rejects a floor request/release for a net the
participant is not a member of** (`FloorArbiter` membership gate). But that gate authorizes the
participant id a client *claims*. With NATS running **anonymous and plaintext** today, any host on the
LAN can:

- connect with no credentials,
- **publish as any `clientId`** (`audio.in.<other>`) and **subscribe to any client's stream**
  (`audio.out.<other>`),
- **impersonate any `participantId`** on `floor.request`/`floor.release`,

so the clearance model is not enforced at the transport, and voice is unencrypted on the wire. Until
this lands, **deploy only on a trusted, isolated lab network.**

## Target design

Three layers, each independent and additive:

1. **Authentication** — every host presents a NATS identity (a `.creds` file: NKey seed + signed JWT,
   the standard NATS artifact). No anonymous connections. This turns the *claimed* `participantId`/
   `clientId` into one the server can pin.
2. **Subject permissions** — per identity, the server restricts publish/subscribe to exactly that
   host's subjects, so a client can only use **its own** `audio.in/out.<self>` and `floor.*`, and only
   the media service may subscribe to `audio.in.>` / publish `floor.events.*` / `audio.out.*`. This is
   what makes impersonation impossible rather than merely application-rejected.
3. **TLS** — encrypt the transport (a private CA on the LAN; optional mutual TLS).

### Subject-permission matrix (the authorization the server config encodes)

| Identity | Publish | Subscribe |
|---|---|---|
| **client `<id>`** | `audio.in.<id>`, `floor.request`, `floor.release` | `audio.out.<id>`, `floor.events.>` |
| **agent `<host>`** | `presence.heartbeat`, KV (`$KV.>` scoped) | `agent.<host>.cmd` (its own command subject) |
| **media service** | `audio.out.>`, `floor.events.>`, KV | `audio.in.>`, `floor.request`, `floor.release`, `cmd.degrade` |
| **manager** | `agent.*.cmd`, `cmd.degrade`, KV (force_tree/configs/endpoints) | `presence.heartbeat`, KV |

(`floor.request`/`floor.release` are shared subjects, so the per-client identity can only *publish*
its own presses; the server still can't bind a publish to a specific `participantId` field — that's why
the application membership gate stays as defence-in-depth.)

## Rollout slices

- **Slice 1 — messaging supports auth + TLS via config. ✅ (this change)**
  `RadioNatsOptions` (bound from the `Nats` section) carries `CredsFile` and `Tls` settings;
  `RadioNatsOpts.Build` layers NATS `AuthOpts`/`TlsOpts` onto the connection, and every host binds it.
  **Opt-in and backward-compatible:** with only `Nats:Url` set the connection is exactly the current
  anonymous, plaintext one, so this changes no behaviour until you provision credentials/certs.
- **Slice 2 — NATS server config + credential provisioning** (needs an infra decision, see below):
  a server configuration (accounts/users + the subject-permission matrix above) under `deploy/nats/`,
  and a way to mint per-host `.creds`. Then set `Nats:CredsFile` on each host.
- **Slice 3 — TLS**: stand up a CA, issue a server cert, set `Nats:Tls:Enabled=true` + `CaFile` on
  every host (and client certs for mutual TLS if desired).

## Configuration (Slice 1)

```jsonc
{
  "Nats": {
    "Url": "nats://srv_brk:4222",
    "CredsFile": "/etc/dasim/media.creds",   // omit ⇒ anonymous (current behaviour)
    "Tls": {
      "Enabled": false,                       // true ⇒ require TLS
      "CaFile": "/etc/dasim/ca.pem",          // trust root for the server cert
      "CertFile": null, "KeyFile": null,      // client cert for mutual TLS (optional)
      "InsecureSkipVerify": false             // lab-only; never in production
    }
  }
}
```

## Decisions for the operator (block Slice 2)

- **Auth model:** full NATS **operator/account/user JWT** (decentralized, `nsc`-managed) vs. simpler
  **server-config accounts with NKey users**. For a single-broker LAN at ≤50 pax, server-config
  accounts are likely enough; JWT scales to multi-broker/rotation. *Recommendation: start with
  server-config accounts.*
- **Per-client identity granularity:** one identity per client id, or a shared "clients" account with
  templated `audio.in/out.{{user}}` permissions (NATS supports `{{...}}` substitution). Templated perms
  avoid minting a creds file per operator.
- **TLS:** internal CA vs. an existing PKI; server-only TLS vs. mutual TLS.

Once you pick the auth model, Slice 2 (the server config + provisioning) is a small, concrete follow-up.
