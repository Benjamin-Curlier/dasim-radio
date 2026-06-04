# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning: [SemVer](https://semver.org/). Releases are tagged `vX.Y.Z`.

## [Unreleased]

### Added
- Solution scaffold (`.slnx`, .NET 10) with Central Package Management and shared build
  settings (analyzers, nullable, warnings-as-errors).
- `Dasim.Radio.Core`: force-tree model and the authoritative floor-control state machine
  (strict pre-emption) with `TimeProvider`.
- `Dasim.Radio.Contracts`: NATS subjects and wire DTOs (primitives only).
- Unit tests (xUnit v3) — 13 passing.
- CI: GitHub Actions (Linux + Windows) + SonarQube analysis.
- Docs: architecture + decision log, technology stack, `CLAUDE.md`.
- Repo hygiene: issue/PR templates, Dependabot, CONTRIBUTING, SECURITY.
- Self-hosted SonarQube `docker-compose`.

### Changed
- Branching model: GitHub Flow + `release/X.Y` maintenance branches (was GitFlow).
- Codec decision: managed Concentus in the client, native libopus (OpusSharp) in the media
  service.
