<#
.SYNOPSIS
    Publishes SharpClaw (Uno desktop + API backend) as a self-contained
    distributable for GitHub Releases.

.DESCRIPTION
    Produces a single folder containing:
      - SharpClaw.Uno.exe      (Uno desktop app, self-contained, R2R)
      - backend\               (API backend, self-contained, R2R)

    Neither project is trimmed: both use reflection-based JSON serialization
    (anonymous types, DTOs) that the IL trimmer cannot preserve.
    Uses CoreCLR runtime (UseMonoRuntime=false) because the Mono runtime
    NuGet packages are not published for .NET 10.

    The Uno app automatically launches the backend as a hidden child process
    (no terminal window required). Double-click SharpClaw.Uno.exe to run.

.PARAMETER Rid
    Runtime identifier. Default: win-x64.
    Supported: win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64.
    Shorthands: "win" is an alias for win-x64.
               "linux" publishes both linux-x64 and linux-arm64.
               "osx" publishes both osx-x64 and osx-arm64.
               "all" publishes every supported RID.
    Note: win-arm64 is NOT supported by Uno desktop (Skia). Use win-x64
    on ARM64 Windows -- it runs under x64 emulation with no issues.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER OutputDir
    Final output directory for the zip. Default: publish\ at repo root.

.PARAMETER SkipZip
    If set, skips creating the zip archive (useful for local testing).

.EXAMPLE
    .\publish-release.ps1
    .\publish-release.ps1 -Rid osx
    .\publish-release.ps1 -Rid all
    .\publish-release.ps1 -Rid linux-x64 -SkipZip
#>
param(
    [string]$Rid = "win-x64",
    [string]$Configuration = "Release",
    [string]$OutputDir = (Join-Path $PSScriptRoot "publish"),
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# -- RID shorthand expansion --
$supportedRids = @(
    "win-x64",
    "linux-x64", "linux-arm64",
    "osx-x64", "osx-arm64"
)
$ridGroups = @{
    "win"   = @("win-x64")
    "linux" = @("linux-x64", "linux-arm64")
    "osx"   = @("osx-x64", "osx-arm64")
    "all"   = $supportedRids
}

if ($ridGroups.ContainsKey($Rid)) {
    $ridsToPublish = $ridGroups[$Rid]
} elseif ($supportedRids -contains $Rid) {
    $ridsToPublish = @($Rid)
} else {
    Write-Error ("RID '$Rid' is not supported for Uno desktop (Skia) builds.`n" +
        "Supported RIDs: $($supportedRids -join ', ')`n" +
        "Shorthands: win, linux, osx, all`n" +
        "Note: On ARM64 Windows, use 'win-x64' -- it runs under x64 emulation with no issues.")
    exit 1
}

$repoRoot   = $PSScriptRoot
$unoProject = Join-Path (Join-Path $repoRoot "SharpClaw.Uno") "SharpClaw.Uno.csproj"

# -- Publish function --
function Publish-SharpClaw {
    param([string]$TargetRid)

    $stageDir = Join-Path $OutputDir "SharpClaw-$TargetRid"
    $zipPath  = Join-Path $OutputDir "SharpClaw-$TargetRid.zip"

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  SharpClaw Release Build" -ForegroundColor Green
    Write-Host "  RID:    $TargetRid" -ForegroundColor Green
    Write-Host "  Config: $Configuration" -ForegroundColor Green
    Write-Host "  Output: $stageDir" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""

    # Clean previous output
    if (Test-Path $stageDir) {
        Write-Host "Cleaning previous build at $stageDir ..." -ForegroundColor Yellow
        Remove-Item $stageDir -Recurse -Force
    }
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    # All Uno desktop targets use the same TFM
    $tfm = "net10.0-desktop"

    # Publish
    Write-Host "Publishing Uno app + bundled backend ..." -ForegroundColor Cyan
    Write-Host "  TFM: $tfm | RID: $TargetRid | Self-contained + R2R (no trimming)" -ForegroundColor DarkGray

    $publishArgs = @(
        "publish", $unoProject,
        "-c", $Configuration,
        "-f", $tfm,
        "-r", $TargetRid,
        "--self-contained",
        "-p:BundleBackend=true",
        "-p:UseMonoRuntime=false",
        "-p:PublishReadyToRun=true",
        "-o", $stageDir
    )

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publish failed for $TargetRid with exit code $LASTEXITCODE"
        return $false
    }

    # Verify backend was bundled
    $backendDir = Join-Path $stageDir "backend"
    $isWin = $TargetRid -like "win-*"
    $backendExe = if ($isWin) {
        Join-Path $backendDir "SharpClaw.Application.API.exe"
    } else {
        Join-Path $backendDir "SharpClaw.Application.API"
    }

    if (-not (Test-Path $backendExe)) {
        Write-Error "Backend executable not found at '$backendExe'. The BundleBackend target may have failed."
        return $false
    }

    Write-Host ""
    Write-Host "Backend bundled at: $backendExe" -ForegroundColor Green

    # -- Strip foreign-platform native libraries to reclaim ~500+ MB --
    # The LLamaSharp platform-conditional packages handle the bulk of this at
    # NuGet-restore time, but leftover runtimes/ directories for non-target
    # RIDs can still sneak in from transitive dependencies. This cleanup pass
    # removes them along with the unused VLC architecture (e.g. win-x86 when
    # publishing for win-x64).
    $ridArch  = ($TargetRid -split '-')[-1]          # x64, arm64, etc.
    $ridOs    = ($TargetRid -split '-')[0]            # win, linux, osx

    # Remove non-target-RID runtimes/ sub-folders from both frontend and backend
    foreach ($runtimesDir in (Get-ChildItem $stageDir -Recurse -Directory -Filter "runtimes" -ErrorAction SilentlyContinue)) {
        foreach ($ridDir in (Get-ChildItem $runtimesDir.FullName -Directory -ErrorAction SilentlyContinue)) {
            $name = $ridDir.Name
            # Keep only folders that match the target OS prefix (win, linux, osx)
            $matchesOs = $name -like "$ridOs-*" -or $name -eq $ridOs
            if (-not $matchesOs) {
                Remove-Item $ridDir.FullName -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    # Remove non-target VLC architecture (e.g. libvlc/win-x86 when publishing win-x64)
    $vlcDir = Join-Path $stageDir "libvlc"
    if (Test-Path $vlcDir) {
        foreach ($archDir in (Get-ChildItem $vlcDir -Directory -ErrorAction SilentlyContinue)) {
            if ($archDir.Name -notlike "*$ridArch*") {
                Remove-Item $archDir.FullName -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    $cleanedSize = [math]::Round(
        ((Get-ChildItem $stageDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1
    )
    Write-Host "  After cleanup:  $cleanedSize MB (stripped foreign-platform natives)" -ForegroundColor DarkGray

    # Summary
    $unoExe = if ($isWin) {
        Join-Path $stageDir "SharpClaw.Uno.exe"
    } else {
        Join-Path $stageDir "SharpClaw.Uno"
    }

    $frontendSize = if (Test-Path $unoExe) {
        [math]::Round((Get-Item $unoExe).Length / 1MB, 1)
    } else { "?" }

    $totalSize = [math]::Round(
        ((Get-ChildItem $stageDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB), 1
    )

    Write-Host ""
    Write-Host "-- Build Summary ($TargetRid) --" -ForegroundColor Green
    Write-Host "  Frontend:   $frontendSize MB" -ForegroundColor White
    Write-Host "  Total size: $totalSize MB" -ForegroundColor White
    Write-Host "  Output:     $stageDir" -ForegroundColor White

    # Zip for GitHub release
    if (-not $SkipZip) {
        Write-Host ""
        Write-Host "Creating zip archive ..." -ForegroundColor Cyan
        Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -Force
        $zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
        Write-Host "  Archive: $zipPath ($zipSize MB)" -ForegroundColor Green
    }

    return $true
}

# -- Run publishes --
$failed = @()
foreach ($rid in $ridsToPublish) {
    $ok = Publish-SharpClaw -TargetRid $rid
    if (-not $ok) { $failed += $rid }
}

# -- Final report --
Write-Host ""
if ($ridsToPublish.Count -gt 1) {
    $succeeded = $ridsToPublish.Count - $failed.Count
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  Published $succeeded / $($ridsToPublish.Count) targets" -ForegroundColor Green
    if ($failed.Count -gt 0) {
        Write-Host "  Failed: $($failed -join ', ')" -ForegroundColor Red
    }
    Write-Host "========================================" -ForegroundColor Green
}
Write-Host "Done. No terminal required to run -- double-click the app executable." -ForegroundColor Green
Write-Host ""

if ($failed.Count -gt 0) { exit 1 }
