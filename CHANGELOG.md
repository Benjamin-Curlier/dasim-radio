# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning: [SemVer](https://semver.org/). Releases are tagged `vX.Y.Z`.

## [Unreleased]

### Added

Phase 1 (foundation):
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

Phase 2 (hosts + transport):
- `Dasim.Radio.Messaging`: NATS.Net wrappers — core audio bus, KV control-plane store,
  floor/presence/degrade signalling, agent-command service — with Testcontainers integration tests.
- `Dasim.Radio.Audio`: Opus codec seam (`IOpusEncoder`/`IOpusDecoder`/factories, `AudioFormat`)
  with `Concentus` (managed, client) and `OpusSharp`/native-libopus (media service) implementations,
  plus a BenchmarkDotNet encode benchmark.
- `Dasim.Radio.Core`: subtree-net topology, mix planner, and the `IMixPolicy` strategy
  (`PriorityOverride` + `Additive`).
- `Dasim.Radio.MediaService`: floor authority host; force-tree provider (KV-watched); force-tree
  priority resolver (priority from the tree, never the client); per-listener routing, mixing
  (override + additive) and quality/clarity degradation. See `docs/routing-mix-model.md`.

### Changed
- Branching model: GitHub Flow + `release/X.Y` maintenance branches (was GitFlow).
- Codec decision: managed Concentus in the client, native libopus (OpusSharp) in the media
  service.
- Floor priority is now derived from the authoritative force tree (replacing the interim
  client-trusting resolver), closing a rank-spoofing gap.
