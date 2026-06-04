# Security policy

## Reporting a vulnerability

**Do not open a public issue for security problems.**

Report privately via GitHub's **"Report a vulnerability"** (Security → Advisories) or by email
to the maintainer. Include affected component, impact, and reproduction steps. You'll get an
acknowledgement and a fix timeline.

## Supported versions

| Version | Supported |
|---|---|
| `main` (latest) | ✅ |
| Tagged releases | Latest minor only |

## Design notes relevant to security

This is a LAN voice stack for a controlled environment. Even so:

- NATS must use **TLS** + decentralized auth (NKeys/JWT, operator/account model).
- The hierarchy is enforced at the broker via **subject permissions** (a member cannot
  subscribe to nets above their clearance) — defence in depth beyond client-side checks.

See [docs/architecture.md](docs/architecture.md) §5.
