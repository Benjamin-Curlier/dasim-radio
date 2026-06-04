#requires -Version 5.1
<#
.SYNOPSIS
    One-shot GitHub setup for Dasim.Radio: create the private repo, push main, apply labels,
    branch protection, starter issues, and enable the wiki.

.DESCRIPTION
    Idempotent where practical. Requires an authenticated GitHub CLI.

.PREREQUISITES
    winget install --id GitHub.cli
    gh auth login        # choose HTTPS; this also configures git credentials

.EXAMPLE
    pwsh ./scripts/github-setup.ps1
    pwsh ./scripts/github-setup.ps1 -RepoName dasim-radio -Visibility private
#>
[CmdletBinding()]
param(
    [string]$RepoName = 'dasim-radio',
    [ValidateSet('private', 'public', 'internal')]
    [string]$Visibility = 'private'
)

$ErrorActionPreference = 'Stop'
# Non-zero exits from native tools (gh/git) must not abort the whole script under PowerShell 7+.
# (Harmless no-op variable on Windows PowerShell 5.1.)
$PSNativeCommandUseErrorActionPreference = $false

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI 'gh' not found. Install it: winget install --id GitHub.cli, then 'gh auth login'."
}

gh auth status | Out-Null
$owner = (gh api user --jq .login).Trim()
$slug = "$owner/$RepoName"
Write-Host "Authenticated as '$owner'. Target repository: $slug" -ForegroundColor Cyan

# 1. Create the repo (push current main) if it does not exist yet.
$exists = $true
try { gh repo view $slug | Out-Null } catch { $exists = $false }
if (-not $exists) {
    Write-Host "Creating $Visibility repo and pushing main..." -ForegroundColor Green
    gh repo create $slug "--$Visibility" --source . --remote origin --push
}
else {
    Write-Host "Repo already exists; ensuring 'origin' and pushing main." -ForegroundColor Yellow
    if (-not (git remote | Select-String -SimpleMatch 'origin')) {
        git remote add origin "https://github.com/$slug.git"
    }
    git push -u origin main
}

# 2. Labels.
$labels = @(
    @{ name = 'area:core';          color = '1d76db'; desc = 'Domain: floor control / hierarchy' }
    @{ name = 'area:messaging';     color = '0e8a16'; desc = 'NATS messaging' }
    @{ name = 'area:audio';         color = '5319e7'; desc = 'Audio capture/playback/codec' }
    @{ name = 'area:media-service'; color = 'b60205'; desc = 'Central media service' }
    @{ name = 'area:client';        color = 'fbca04'; desc = 'Avalonia client' }
    @{ name = 'area:agent';         color = '006b75'; desc = 'Per-post daemon' }
    @{ name = 'area:manager';       color = '0052cc'; desc = 'Blazor manager' }
    @{ name = 'area:ci';            color = 'c5def5'; desc = 'CI / build / tooling' }
    @{ name = 'area:docs';          color = 'd4c5f9'; desc = 'Documentation' }
    @{ name = 'type:bug';          color = 'd73a4a'; desc = 'Something is broken' }
    @{ name = 'type:feature';      color = 'a2eeef'; desc = 'New capability' }
    @{ name = 'type:task';         color = 'cfd3d7'; desc = 'Implementation work' }
    @{ name = 'type:security';     color = 'b60205'; desc = 'Security-relevant' }
    @{ name = 'phase:2';           color = 'ededed'; desc = 'Phase 2: hosts + transport' }
)
Write-Host "Applying labels..." -ForegroundColor Green
foreach ($l in $labels) {
    gh label create $l.name --color $l.color --description $l.desc --force | Out-Null
}

# 3. Branch protection on main (solo GitHub Flow: require CI, allow self-merge, linear history).
Write-Host "Protecting 'main'..." -ForegroundColor Green
$protection = @{
    required_status_checks        = @{
        strict   = $true
        contexts = @('Build & Test (ubuntu-latest)', 'Build & Test (windows-latest)')
    }
    enforce_admins                = $false
    required_pull_request_reviews = $null
    restrictions                  = $null
    required_linear_history       = $true
    allow_force_pushes            = $false
    allow_deletions               = $false
} | ConvertTo-Json -Depth 6
$protection | gh api -X PUT "repos/$slug/branches/main/protection" `
    -H 'Accept: application/vnd.github+json' --input - | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Warning ("Branch protection not applied. Classic protection on a PRIVATE repo needs a paid plan. " +
        "Use a Ruleset in the GitHub UI (Settings -> Rules), or make the repo public.")
}

# 4. Enable the wiki.
gh api -X PATCH "repos/$slug" -f has_wiki=true | Out-Null

# 5. Starter issues (Phase 2 backlog).
$issues = @(
    @{ title = 'Phase 2: Dasim.Radio.Messaging (NATS.Net wrappers)';
       labels = 'area:messaging,phase:2';
       body = 'NATS.Net wrappers: core (audio), JetStream/KV (force_tree, configs, presence, floor_state), Services (agent commands). Add Dasim.Radio.Integration.Tests with Testcontainers NATS.' }
    @{ title = 'Phase 2: Dasim.Radio.Audio (capture/playback + codec abstraction)';
       labels = 'area:audio,phase:2';
       body = 'IOpusEncoder/IOpusDecoder seam. Client = Concentus (managed). OwnAudioSharp for capture/playback. Device enumeration on Windows + Linux.' }
    @{ title = 'Phase 2: Dasim.Radio.MediaService - libopus PoC + benchmark';
       labels = 'area:media-service,phase:2';
       body = 'Floor authority + per-listener mix + per-listener DSP degradation. Native libopus via OpusSharp/OpusSharp.Natives. Benchmark ~50 encodes/20ms with BenchmarkDotNet on real CPU.' }
    @{ title = 'Phase 2: Dasim.Radio.Agent (daemon)';
       labels = 'area:agent,phase:2';
       body = 'systemd/Windows Service. Presence heartbeat (discovery), handle agent.<host>.cmd (launch/stop/reconfigure) via NATS Services. Consider Native AOT.' }
    @{ title = 'Phase 2: Dasim.Radio.Client (Avalonia)';
       labels = 'area:client,phase:2';
       body = 'Avalonia UI, audio in/out device selection, global PTT via SharpHook. RISK: SharpHook is X11-only - validate Wayland behaviour first.' }
    @{ title = 'Phase 2: Dasim.Radio.Manager (Blazor)';
       labels = 'area:manager,phase:2';
       body = 'Config CRUD, force-tree import from NATS KV, post discovery, launch/stop clients, degrade commands.' }
    @{ title = 'Phase 2: NATS security (TLS + NKey/JWT + subject permissions)';
       labels = 'area:messaging,type:security,phase:2';
       body = 'TLS, decentralized auth, and map the hierarchy onto subject permissions so a member cannot subscribe above clearance.' }
    @{ title = 'Spike: global PTT hotkey under Wayland vs X11';
       labels = 'area:client,phase:2';
       body = 'libuiohook is X11-only. Test unfocused global hotkey on target distros; decide mitigation (require Xorg, detect XDG_SESSION_TYPE, or focused-only on Wayland).' }
)
Write-Host "Creating starter issues..." -ForegroundColor Green
foreach ($i in $issues) {
    gh issue create --title $i.title --body $i.body --label $i.labels | Out-Null
}

Write-Host "Done. Repository ready: https://github.com/$slug" -ForegroundColor Cyan
Write-Host "Next, enable SonarCloud (free < 50k LoC) once you've created the project at https://sonarcloud.io:" -ForegroundColor Cyan
Write-Host "  gh secret   set SONAR_TOKEN          # paste the SonarCloud token"
Write-Host "  gh variable set SONAR_ORGANIZATION   --body <your-sonar-org>"
Write-Host "  gh variable set SONAR_PROJECT_KEY    --body <your-project-key>"
