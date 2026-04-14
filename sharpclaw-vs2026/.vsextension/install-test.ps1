# Install & test script for SharpClaw VS2026 Extension
# Uses MSBuild.exe (full framework) from the VS 2026 installation for build + deploy.
# SDK-style VSIX projects cannot deploy via `dotnet build` (VSSDK explicitly blocks it),
# but MSBuild.exe can invoke the full VSSDK targets that:
#   1. Generate the .pkgdef from assembly registration attributes
#   2. Create the .vsix container
#   3. Deploy to the VS Extensions directory
#   4. Register the extension in VS private registry (EnableLoadingAllExtensions + EnableExtension)
#
# Usage:
#   .\install-test.ps1                       # Build Release, deploy to experimental instance, launch
#   .\install-test.ps1 -Configuration Debug  # Build Debug
#   .\install-test.ps1 -NoLaunch             # Deploy without launching VS
#   .\install-test.ps1 -MainInstance          # Deploy to the main VS instance (not experimental)
#   .\install-test.ps1 -SkipBuild            # Skip build, use previous deploy
#   .\install-test.ps1 -Clean                # Remove bin/obj before building
#   .\install-test.ps1 -Uninstall            # Remove the extension
#   .\install-test.ps1 -Reset                # Uninstall + clear MEF cache + clean bin/obj, then redeploy

param(
    [string]$Configuration = "Release",
    [switch]$NoLaunch,
    [switch]$MainInstance,
    [switch]$SkipBuild,
    [switch]$Clean,
    [switch]$Uninstall,
    [switch]$Reset
)

$ErrorActionPreference = "Stop"

# ── Extension metadata (only used for cleanup / identification) ───────────────

$ExtensionId     = "SharpClaw.VS2026Extension.d5e3a8f1-4c2b-4e9d-8f1a-2b3c4d5e6f7a"
$AssemblyName    = "SharpClaw.VS2026Extension"

$ProjectDir  = Split-Path $PSScriptRoot -Parent
$CsprojPath  = Join-Path $ProjectDir "SharpClaw.VS2026Extension.csproj"
$RootSuffix  = if ($MainInstance) { $null } else { "Exp" }

# ── Locate VS 2026 via vswhere ──────────────────────────────────────────────

$vswherePath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswherePath)) {
    Write-Error "vswhere.exe not found at $vswherePath. Is Visual Studio installed?"
    exit 1
}

$vsInstallPath = & $vswherePath -version "[18.0,19.0)" -products * -requires Microsoft.Component.MSBuild -property installationPath -latest
if (-not $vsInstallPath) {
    Write-Error "Visual Studio 2026 (version 18.x) installation not found."
    exit 1
}

$vsInstanceId = & $vswherePath -version "[18.0,19.0)" -products * -requires Microsoft.Component.MSBuild -property instanceId -latest

Write-Host "Found VS 2026 at: $vsInstallPath" -ForegroundColor Cyan
Write-Host "Instance ID: $vsInstanceId" -ForegroundColor Gray

$devenv  = Join-Path $vsInstallPath "Common7\IDE\devenv.exe"
$msbuild = Join-Path $vsInstallPath "MSBuild\Current\Bin\MSBuild.exe"

if (-not (Test-Path $msbuild)) {
    Write-Error "MSBuild.exe not found at $msbuild. VS installation may be incomplete."
    exit 1
}

# ── Resolve VS instance data directory ───────────────────────────────────────

$suffix = if ($RootSuffix) { $RootSuffix } else { "" }

$instanceDir = Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft\VisualStudio") -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq "18.0_${vsInstanceId}${suffix}" } |
    Select-Object -First 1

if (-not $instanceDir) {
    if ($RootSuffix) {
        Write-Host "Experimental instance directory not found yet. MSBuild deploy will create it." -ForegroundColor Yellow
    } else {
        Write-Error "Cannot find VS instance directory for 18.0_${vsInstanceId}"
        exit 1
    }
}

$extensionsRoot = if ($instanceDir) { Join-Path $instanceDir.FullName "Extensions" } else { $null }

Write-Host "Extensions root: $(if ($extensionsRoot) { $extensionsRoot } else { '(will be created)' })" -ForegroundColor Gray

# ── Uninstall ────────────────────────────────────────────────────────────────

function Close-TargetVSInstance {
    # Gracefully close the VS instance that has our extension loaded.
    # Experimental: match devenv processes launched with /rootSuffix Exp.
    # Main: warn the user (we don't auto-kill their primary IDE).

    if ($MainInstance) {
        # For the main instance, just check if any devenv is locking the DLL
        $lockTarget = if ($extensionsRoot) {
            Join-Path $extensionsRoot "SharpClaw Team\SharpClaw for Visual Studio 2026\1.0.0\$AssemblyName.dll"
        } else { $null }

        if ($lockTarget -and (Test-Path $lockTarget)) {
            $locked = $false
            try {
                [IO.File]::Open($lockTarget, 'Open', 'ReadWrite', 'None').Dispose()
            } catch {
                $locked = $true
            }
            if ($locked) {
                Write-Host "  ⚠ The main VS instance appears to have the extension loaded." -ForegroundColor Yellow
                Write-Host "  Please close Visual Studio manually before re-deploying." -ForegroundColor Yellow
                Write-Error "Cannot remove extension files while VS is running."
            }
        }
        return
    }

    # Experimental instance — find devenv processes whose command line contains /rootSuffix
    $expProcesses = Get-CimInstance Win32_Process -Filter "Name='devenv.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match '/rootSuffix' }

    if (-not $expProcesses) {
        Write-Host "  No experimental VS instance running." -ForegroundColor Gray
        return
    }

    foreach ($proc in $expProcesses) {
        Write-Host "  Closing VS Experimental Instance (PID $($proc.ProcessId))..." -ForegroundColor Yellow
        $p = Get-Process -Id $proc.ProcessId -ErrorAction SilentlyContinue
        if (-not $p -or $p.HasExited) { continue }

        # Try graceful close first (WM_CLOSE via CloseMainWindow)
        $null = $p.CloseMainWindow()
        $exited = $p.WaitForExit(15000)  # 15 seconds

        if (-not $exited) {
            Write-Host "  VS did not exit gracefully — terminating." -ForegroundColor DarkYellow
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
        }

        Write-Host "  VS Experimental Instance closed." -ForegroundColor Green
    }

    # Brief pause for file handles to release
    Start-Sleep -Milliseconds 500
}

function Remove-Extension {
    # Remove all previous SharpClaw deployments — matches by extension ID in
    # manifests, assembly name, or known publisher/display-name paths.
    # This catches the current xcopy layout AND any stale F5-debug deployments
    # that used a different publisher name or folder structure.

    if (-not $extensionsRoot -or -not (Test-Path $extensionsRoot)) {
        Write-Host "  No extensions directory found." -ForegroundColor Gray
        return
    }

    $removed = 0

    if (Test-Path $extensionsRoot) {
        # Walk every leaf version folder looking for our DLL or extension ID
        Get-ChildItem $extensionsRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $pubDir = $_
            Get-ChildItem $pubDir.FullName -Directory -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
                $candidate = $_.FullName
                $isOurs   = $false

                # Match by assembly DLL name
                if (Test-Path (Join-Path $candidate "$AssemblyName.dll")) {
                    $isOurs = $true
                }
                # Match by old F5-debug assembly name (SharpClaw.VS2026.dll)
                if (Test-Path (Join-Path $candidate "SharpClaw.VS2026.dll")) {
                    $isOurs = $true
                }
                # Match by extension ID inside manifest.json
                $mj = Join-Path $candidate "manifest.json"
                if ((-not $isOurs) -and (Test-Path $mj)) {
                    $mjText = Get-Content $mj -Raw -ErrorAction SilentlyContinue
                    if ($mjText -match "SharpClaw") { $isOurs = $true }
                }

                if ($isOurs) {
                    Remove-Item $candidate -Recurse -Force
                    Write-Host "  Removed: $candidate" -ForegroundColor DarkYellow
                    $removed++
                }
            }

            # Prune empty ancestor folders up to the Extensions root
            $walk = $pubDir.FullName
            while ($walk -ne $extensionsRoot -and (Test-Path $walk)) {
                if ((Get-ChildItem $walk -ErrorAction SilentlyContinue).Count -eq 0) {
                    Remove-Item $walk -Force
                    $walk = Split-Path $walk -Parent
                } else {
                    break
                }
            }
        }
    }

    if ($removed -eq 0) {
        Write-Host "  No previous SharpClaw deployments found." -ForegroundColor Gray
    } else {
        Write-Host "  Cleaned up $removed previous deployment(s)." -ForegroundColor Green
    }
}

function Clear-MefCache {
    if (-not $instanceDir) { return }
    $cachePath = Join-Path $instanceDir.FullName "ComponentModelCache"
    if (Test-Path $cachePath) {
        Remove-Item $cachePath -Recurse -Force
        Write-Host "Cleared MEF component cache." -ForegroundColor Green
    }
}

function Clean-BuildArtifacts {
    foreach ($dir in "bin", "obj") {
        $p = Join-Path $ProjectDir $dir
        if (Test-Path $p) {
            Remove-Item $p -Recurse -Force
            Write-Host "  Removed $dir/" -ForegroundColor DarkYellow
        }
    }
}

if ($Uninstall -and -not $Reset) {
    Write-Host "`nUninstalling SharpClaw extension..." -ForegroundColor Yellow
    Close-TargetVSInstance
    Remove-Extension
    Clear-MefCache
    Write-Host "Done. Restart VS to complete removal." -ForegroundColor Green
    exit 0
}

if ($Reset) {
    Write-Host "`nResetting SharpClaw extension (full clean)..." -ForegroundColor Yellow
    Close-TargetVSInstance
    Remove-Extension
    Clear-MefCache
    Clean-BuildArtifacts
    Write-Host "Clean slate. Proceeding with fresh install...`n" -ForegroundColor Green
}

# ── Clean previous deployments ───────────────────────────────────────────────
# Always remove stale extension files before deploying to avoid conflicts.

if (-not $SkipBuild) {
    Write-Host "`nRemoving previous extension deployments..." -ForegroundColor Yellow
    Close-TargetVSInstance
    Remove-Extension
}

# ── Build + Deploy with MSBuild.exe ──────────────────────────────────────────
# MSBuild.exe (full framework) runs the VSSDK targets that `dotnet build` cannot:
#   - GeneratePkgDef: scans assembly attributes → .pkgdef
#   - CreateVsixContainer: packages everything into .vsix
#   - DeployVsixExtensionFiles: extracts to Extensions dir, calls
#       EnableLoadingAllExtensions + EnableExtension (registers in privateregistry.bin)

if ($Clean -and -not $SkipBuild) {
    Write-Host "`nCleaning build artifacts..." -ForegroundColor Yellow
    Clean-BuildArtifacts
}

if (-not $SkipBuild) {
    Write-Host "`nBuilding and deploying with MSBuild.exe ($Configuration)..." -ForegroundColor Cyan

    $msbuildArgs = @(
        "`"$CsprojPath`""
        "/restore"
        "/p:Configuration=$Configuration"
        "/p:CreateVsixContainer=true"
        "/p:DeployExtension=true"
        "/p:DeployVSTemplates=false"
    )

    if ($RootSuffix) {
        $msbuildArgs += "/p:RootSuffix=$RootSuffix"
    }

    $msbuildArgs += "/v:minimal"

    Write-Host "  $msbuild" -ForegroundColor DarkGray
    Write-Host "    $($msbuildArgs -join ' ')" -ForegroundColor DarkGray

    & $msbuild @msbuildArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Error "MSBuild failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host "`n✅ Build + deploy succeeded." -ForegroundColor Green
} else {
    Write-Host "`nSkipping build (using previous deployment)." -ForegroundColor Yellow
}

# ── Clear MEF cache so VS discovers the new/updated extension ────────────────

Clear-MefCache

# ── Summary ──────────────────────────────────────────────────────────────────

# Re-resolve instance dir (MSBuild deploy may have created it)
if (-not $instanceDir) {
    $instanceDir = Get-ChildItem (Join-Path $env:LOCALAPPDATA "Microsoft\VisualStudio") -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "18.0_${vsInstanceId}${suffix}" } |
        Select-Object -First 1
    if ($instanceDir) {
        $extensionsRoot = Join-Path $instanceDir.FullName "Extensions"
    }
}

$target = if ($RootSuffix) { "experimental instance ($RootSuffix)" } else { "main instance" }
Write-Host "`n✅ Extension deployed to $target." -ForegroundColor Green

if ($extensionsRoot -and (Test-Path $extensionsRoot)) {
    $deployedDir = Get-ChildItem $extensionsRoot -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "$AssemblyName.dll") } |
        Select-Object -First 1

    if ($deployedDir) {
        Write-Host "   Location: $($deployedDir.FullName)" -ForegroundColor Gray
        Write-Host "   Files:" -ForegroundColor Gray
        Get-ChildItem $deployedDir.FullName | ForEach-Object { Write-Host "     $($_.Name)" -ForegroundColor Gray }
    }
}

# ── Launch ───────────────────────────────────────────────────────────────────

if (-not $NoLaunch) {
    Write-Host "`nLaunching VS 2026..." -ForegroundColor Cyan
    $launchArgs = @()
    if ($RootSuffix) { $launchArgs += "/rootSuffix"; $launchArgs += $RootSuffix }

    Start-Process $devenv -ArgumentList $launchArgs
    Write-Host "VS 2026 started. Look for SharpClaw under Tools menu." -ForegroundColor Green
} else {
    Write-Host "`nRestart VS to load the extension:" -ForegroundColor Yellow
    if ($RootSuffix) {
        Write-Host "  `"$devenv`" /rootSuffix $RootSuffix" -ForegroundColor White
    } else {
        Write-Host "  `"$devenv`"" -ForegroundColor White
    }
}
