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
        Write-Warning "Working tree has uncommitted changes — switching to '$Branch' may fail:"
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

    # --- Optional: build HSM Agent (mirrors CI; Windows-only, needs vcpkg) ---
    if ($IncludeAgent) {
        if (-not $env:VCPKG_ROOT) {
            throw "-IncludeAgent requires VCPKG_ROOT env var pointing to a vcpkg checkout."
        }
        if (-not (Test-Tool "cmake")) {
            throw "-IncludeAgent requires CMake on PATH."
        }
        Write-Host "Building HSM Agent into wwwroot (vcpkg + curl)..."
        cmake -S src/agent -B build/agent "-DCMAKE_TOOLCHAIN_FILE=$env:VCPKG_ROOT/scripts/buildsystems/vcpkg.cmake"
        if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }
        cmake --build build/agent --config Release --parallel
        if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }
        $exe = Get-ChildItem -Recurse -Path build/agent -Filter hsm-agent.exe | Select-Object -First 1
        if (-not $exe) { throw "hsm-agent.exe not found after build" }
        New-Item -ItemType Directory -Force -Path src/server/HSMServer/wwwroot/agent | Out-Null
        Copy-Item $exe.FullName -Destination src/server/HSMServer/wwwroot/agent/hsm-agent.exe -Force
        Write-Host "Staged agent: $($exe.FullName)"
    } else {
        Write-Warning "-IncludeAgent not set — /api/agent/installer will 503. Pass the flag to mirror CI exactly."
    }

    # --- Publish server to a folder, then build the image with plain `docker build` ---
    # We avoid the SDK's DefaultContainer profile because its HTTP base-image fetch
    # breaks on Windows Docker Desktop (docker-credential-desktop / CONTAINER1008).
    $publishDir = Join-Path $repoRoot "build" "local-publish"
    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }

    Write-Host "Publishing $ServerProject to $publishDir ..."
    dotnet publish $ServerProject `
        -c Release `
        --os linux --arch x64 `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $dockerfile = Join-Path $repoRoot "docker_scripts" "HSMserver" "Dockerfile.local"
    if (-not (Test-Path $dockerfile)) {
        throw "Missing Dockerfile: $dockerfile"
    }

    Write-Host "Building Docker image ${ImageName}:$ImageTag ..."
    docker build -t "${ImageName}:${ImageTag}" -f $dockerfile $publishDir
    if ($LASTEXITCODE -ne 0) { throw "docker build failed with exit code $LASTEXITCODE" }

    Write-Host ""
    Write-Host "Built: ${ImageName}:$ImageTag" -ForegroundColor Green

    if ($NoRun) {
        Write-Host "-NoRun set — skipping docker compose up."
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
