[CmdletBinding()]
param(
    [string]$Branch = "master",
    [string]$ImageTag = "main-local",
    [string]$ImageName = "hsmonitoring/hierarchical_sensor_monitoring",
    [string]$DepsImage = "index.docker.io/hsmonitoring/hierarchical_sensor_monitoring_deps:latest",
    [string]$ServerProject = "src/server/HSMServer/HSMServer.csproj",
    [switch]$IncludeAgent,
    [switch]$NoRun,
    [switch]$StayOnBranch
)

# Builds and runs the HSMServer Docker container locally from a given branch,
# reproducing the output of the publish-docker-image step in
# .github/workflows/server-build.yml.
#
# The CI workflow uses the .NET SDK's DefaultContainer publish profile, but on
# Windows Docker Desktop that path fails with CONTAINER1008 ("credentials not
# found in native keychain") because the SDK's HTTP base-image fetch goes
# through docker-credential-desktop, which rejects anonymous DockerHub pulls.
# So instead we do a plain `dotnet publish` to a folder and feed it to
# `docker build` using docker_scripts/HSMserver/Dockerfile.local, which has
# the same base image (hierarchical_sensor_monitoring_deps:latest) and the
# same ENTRYPOINT as the CI-built image.
#
# Then starts the container with docker-compose.yml + a local override that pins the tag.

$ErrorActionPreference = "Stop"

function Test-Tool([string]$name) {
    return [bool](Get-Command $name -ErrorAction SilentlyContinue)
}

function Invoke-Git {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$GitArgs
    )
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') failed with exit code $LASTEXITCODE"
    }
}

# --- Dependency checks ---
foreach ($tool in @("git", "dotnet", "docker", "node", "npm")) {
    if (-not (Test-Tool $tool)) {
        throw "Required tool not found on PATH: $tool. .NET 8 SDK, Docker Desktop, Node 20 LTS are expected."
    }
}

$dockerServer = docker version --format "{{.Server.Version}}" 2>$null
if (-not $dockerServer) {
    throw "Docker daemon not reachable. Start Docker Desktop first."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
$origBranch = $null
try {
    $origBranch = Invoke-Git rev-parse --abbrev-ref HEAD
    Write-Host "Repo:       $repoRoot"
    Write-Host "Branch was: $origBranch"
    Write-Host "Target:     $Branch"
    Write-Host "Image:      ${ImageName}:$ImageTag"
    Write-Host ""

    # --- Worktree hygiene ---
    $dirty = (git status --porcelain) -join "`n"
    if ($dirty -and $origBranch -ne $Branch) {
        Write-Warning "Working tree has uncommitted changes - switching to '$Branch' may fail:"
        Write-Host $dirty
        $confirm = Read-Host "Continue anyway? (y/N)"
        if ($confirm -ne "y") { throw "Aborted by user." }
    }

    # --- Sync target branch ---
    if ($origBranch -ne $Branch) {
        Write-Host "Checking out '$Branch'..."
        Invoke-Git checkout $Branch
    }
    Write-Host "Fetching origin/$Branch..."
    Invoke-Git fetch origin $Branch
    Invoke-Git merge --ff-only "origin/$Branch"

    # --- Pull deps base image so we don't silently reuse a stale local copy ---
    Write-Host "Pulling base deps image: $DepsImage"
    docker pull $DepsImage
    if ($LASTEXITCODE -ne 0) { throw "docker pull failed for $DepsImage" }

    # --- Stage the HSM Agent: pinned release by default (mirrors CI, #1298); -IncludeAgent builds
    # --- it from source instead (dev override; Windows-only, needs vcpkg).
    $agentDir = Join-Path $repoRoot "src/server/HSMServer/wwwroot/agent"
    if ($IncludeAgent) {
        if (-not $env:VCPKG_ROOT) {
            throw "-IncludeAgent requires VCPKG_ROOT env var pointing to a vcpkg checkout."
        }
        if (-not (Test-Tool "cmake")) {
            throw "-IncludeAgent requires CMake on PATH."
        }
        Write-Host "Building HSM Agent from source into wwwroot (vcpkg + curl)..."
        cmake -S src/agent -B build/agent "-DCMAKE_TOOLCHAIN_FILE=$env:VCPKG_ROOT/scripts/buildsystems/vcpkg.cmake"
        if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }
        cmake --build build/agent --config Release --parallel
        if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }
        $exe = Get-ChildItem -Recurse -Path build/agent -Filter hsm-agent.exe | Select-Object -First 1
        if (-not $exe) { throw "hsm-agent.exe not found after build" }
        New-Item -ItemType Directory -Force -Path $agentDir | Out-Null
        Copy-Item $exe.FullName -Destination (Join-Path $agentDir "hsm-agent.exe") -Force
        # Same pairing CI enforces: the staged version must describe the staged exe, or the update
        # directive on the data path advertises a version nobody has (#1266).
        $match = Select-String -Path src/agent/CMakeLists.txt -Pattern 'project\(HsmAgent VERSION ([0-9][^\s)]*)' | Select-Object -First 1
        if (-not $match) { throw "could not parse 'project(HsmAgent VERSION ...)' from src/agent/CMakeLists.txt" }
        $agentVersion = $match.Matches[0].Groups[1].Value
        Set-Content -Path (Join-Path $agentDir "version.txt") -Value $agentVersion -NoNewline -Encoding ascii
        Write-Host "Staged agent $agentVersion (built from source): $($exe.FullName)"
    } else {
        # Same source of truth as server-build.yml: download the release named by the pin file and
        # verify its checksum, so the local image carries the exact released bytes.
        $pin = (Get-Content (Join-Path $repoRoot "src/server/HSMServer/agent-release.txt") -Raw).Trim()
        if (-not (Test-Tool "gh")) {
            Write-Warning "gh CLI not found — cannot download pinned agent release agent-v$pin; /api/agent/installer will 503. Install gh (and 'gh auth login') or pass -IncludeAgent."
        } else {
            Write-Host "Downloading pinned agent release agent-v$pin ..."
            New-Item -ItemType Directory -Force -Path $agentDir | Out-Null
            # Explicit repo: the release lives upstream, and gh must not resolve a fork's origin instead.
            gh release download "agent-v$pin" --repo SoftFx/Hierarchical-Sensor-Monitoring -p hsm-agent.exe -p hsm-agent.exe.sha256 -D $agentDir --clobber
            if ($LASTEXITCODE -ne 0) { throw "gh release download agent-v$pin failed — does the release exist and is gh authenticated?" }
            $shaFile = Join-Path $agentDir "hsm-agent.exe.sha256"
            $expected = ((Get-Content $shaFile -Raw).Trim() -split '\s+')[0]
            $actual = (Get-FileHash (Join-Path $agentDir "hsm-agent.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($expected -ne $actual) { throw "sha256 mismatch for agent-v${pin}: release says $expected, file is $actual" }
            Remove-Item $shaFile
            Set-Content -Path (Join-Path $agentDir "version.txt") -Value $pin -NoNewline -Encoding ascii
            Write-Host "Staged agent $pin (sha256 $actual)"
        }
    }

    # --- Publish server to a folder, then build the image with plain `docker build` ---
    # We avoid the SDK's DefaultContainer profile because its HTTP base-image fetch
    # breaks on Windows Docker Desktop (docker-credential-desktop / CONTAINER1008).
    $publishDir = Join-Path (Join-Path $repoRoot "build") "local-publish"
    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }

    Write-Host "Publishing $ServerProject to $publishDir ..."
    dotnet publish $ServerProject `
        -c Release `
        --os linux --arch x64 `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $dockerfile = Join-Path (Join-Path (Join-Path $repoRoot "docker_scripts") "HSMserver") "Dockerfile.local"
    if (-not (Test-Path $dockerfile)) {
        throw "Missing Dockerfile: $dockerfile"
    }

    Write-Host "Building Docker image ${ImageName}:$ImageTag ..."
    docker build -t "${ImageName}:${ImageTag}" -f $dockerfile $publishDir
    if ($LASTEXITCODE -ne 0) { throw "docker build failed with exit code $LASTEXITCODE" }

    Write-Host ""
    Write-Host "Built: ${ImageName}:$ImageTag" -ForegroundColor Green

    if ($NoRun) {
        Write-Host "-NoRun set - skipping docker compose up."
        return
    }

    # --- Ensure volume mount dirs exist (otherwise Docker creates them as root) ---
    foreach ($vol in @("Logs", "Config", "Databases", "DatabasesBackups")) {
        New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot $vol) | Out-Null
    }

    # --- Local compose override pins the image tag without touching committed docker-compose.yml ---
    $overridePath = Join-Path $repoRoot "docker-compose.local.yml"
    @"
# Auto-generated by scripts/local-docker-build.ps1. Safe to delete; not committed (see .gitignore).
services:
  app:
    image: '${ImageName}:${ImageTag}'
"@ | Set-Content -Path $overridePath -Encoding utf8

    # --- Stop any existing project containers, then start fresh ---
    Write-Host "Stopping any existing hsm-server container..."
    docker compose -f docker-compose.yml -f docker-compose.local.yml down --remove-orphans 2>$null | Out-Null

    Write-Host "Starting container..."
    docker compose -f docker-compose.yml -f docker-compose.local.yml up -d
    if ($LASTEXITCODE -ne 0) { throw "docker compose up failed." }

    Write-Host ""
    Write-Host "HSM Server is running:" -ForegroundColor Green
    Write-Host "  Web UI:      https://localhost:44333"
    Write-Host "  Sensor API:  https://localhost:44330"
    Write-Host "  Logs:        $repoRoot\Logs"
    Write-Host "  Stop:        docker compose -f docker-compose.yml -f docker-compose.local.yml down"
}
finally {
    if (-not $StayOnBranch -and $origBranch -and $origBranch -ne $Branch) {
        Write-Host "Returning to original branch '$origBranch'..."
        try { Invoke-Git checkout $origBranch } catch { Write-Warning $_ }
    }
    Pop-Location
}
