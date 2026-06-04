# Self-hosted SonarQube (LAN) — on-prem fallback

> The project's primary analysis runs on **SonarCloud** (free tier, see the root README).
> This self-hosted **SonarQube Community Build** is the on-prem fallback if the code must
> stay entirely on the LAN.

## Run

```bash
# Linux host (required once, otherwise Elasticsearch won't start):
sudo sysctl -w vm.max_map_count=524288
sudo sysctl -w fs.file-max=131072

export SONAR_DB_PASSWORD='<a strong password>'
docker compose -f docker/sonarqube/docker-compose.yml up -d
```

Open `http://<lan-host>:9000` (default admin/admin → change it). Create a project
`Dasim.Radio` and a **project analysis token**.

## Analyse from CI or locally

```bash
dotnet sonarscanner begin /k:"Dasim.Radio" \
  /d:sonar.host.url="http://<lan-host>:9000" \
  /d:sonar.token="$SONAR_TOKEN" \
  /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml"

dotnet build Dasim.Radio.slnx -c Release
dotnet test  Dasim.Radio.slnx -c Release --no-build \
  /p:CollectCoverage=true /p:CoverletOutputFormat=opencover \
  /p:CoverletOutput=./TestResults/coverage.opencover.xml

dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
```

`dotnet-sonarscanner` needs Java 17+ on the runner. The existing GitHub Actions workflow
([../../.github/workflows/ci.yml](../../.github/workflows/ci.yml)) wires this up and skips
gracefully until `SONAR_TOKEN` / `SONAR_HOST_URL` secrets are set.

## Limits of Community Build

- Main branch only; **no pull-request decoration** (inline checks/comments).
- If PR decoration is required and cloud is acceptable, **SonarCloud** has a free tier for
  private repos (< 50K LOC) — but code is analysed in the cloud, not on the LAN.
