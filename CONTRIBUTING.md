# Contributing

All code, comments, commits and PRs are in **English**.

## Workflow (GitHub Flow)

1. Branch off `main`: `feature/<short-name>` (or `fix/`, `chore/`, `docs/`).
2. Keep the change small and the build green.
3. Open a PR into `main`. CI (Linux + Windows) and SonarQube must pass.
4. Squash-merge. `main` stays releasable; releases are tagged `vX.Y.Z`.
5. A `release/X.Y` branch is created only to patch a version already deployed in the field.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `chore:`,
`docs:`, `test:`, `refactor:`, `ci:`. Example: `feat(core): add net-priority resolution`.

## Build & test

```bash
dotnet build Dasim.Radio.slnx -c Release   # must be 0 warnings / 0 errors
dotnet test  Dasim.Radio.slnx -c Release
```

## Conventions

- File-scoped namespaces; `sealed` by default; nullable enabled; `_camelCase` private fields.
- No `DateTime.Now` — inject `TimeProvider`.
- Strongly-typed ids/values across the domain (no raw strings/ints).
- `Dasim.Radio.Contracts` stays primitive-only (no dependency on `Core`).
- Central Package Management: add deps with `dotnet add <project> package <id>` — never
  hardcode versions in csproj.
- Warnings are errors; keep the build clean. New domain rules ship with unit tests.

See [CLAUDE.md](CLAUDE.md) and [docs/architecture.md](docs/architecture.md) before structural
changes.
