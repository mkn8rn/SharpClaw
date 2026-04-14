# Publish script for SharpClaw VS2026 Extension
# Builds a distributable .vsix package via `dotnet build`.
# Deploy is explicitly disabled — the output is for manual install or CI/CD.
#
# Usage:
#   .\publish.ps1                       # Build Release .vsix
#   .\publish.ps1 -Configuration Debug  # Build Debug .vsix
#   .\publish.ps1 -Clean                # Clean bin/obj before building

param(
    [string]$Configuration = "Release",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$ProjectDir  = Split-Path $PSScriptRoot -Parent
$CsprojPath  = Join-Path $ProjectDir "SharpClaw.VS2026Extension.csproj"
$AssemblyName = "SharpClaw.VS2026Extension"

# ── Optional clean ───────────────────────────────────────────────────────────

if ($Clean) {
    foreach ($dir in "bin", "obj") {
        $p = Join-Path $ProjectDir $dir
        if (Test-Path $p) {
            Remove-Item $p -Recurse -Force
            Write-Host "Removed $dir/" -ForegroundColor DarkYellow
        }
    }
}

# ── Build ────────────────────────────────────────────────────────────────────

Write-Host "Building $AssemblyName ($Configuration)..." -ForegroundColor Cyan

dotnet build $CsprojPath `
    -c $Configuration `
    /p:CreateVsixContainer=true `
    /p:DeployExtension=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# ── Output ───────────────────────────────────────────────────────────────────

$vsixPath = Join-Path $ProjectDir "bin\$Configuration\net472\$AssemblyName.vsix"

if (Test-Path $vsixPath) {
    $sizeKB = [math]::Round((Get-Item $vsixPath).Length / 1KB, 1)
    Write-Host "`n✅ VSIX package created:" -ForegroundColor Green
    Write-Host "   $vsixPath" -ForegroundColor White
    Write-Host "   Size: $sizeKB KB" -ForegroundColor Gray

    Write-Host "`nTo install manually:" -ForegroundColor Yellow
    Write-Host "  1. Close all Visual Studio instances" -ForegroundColor Gray
    Write-Host "  2. Double-click the .vsix file" -ForegroundColor Gray
    Write-Host "  3. Restart Visual Studio 2026" -ForegroundColor Gray
    Write-Host "`nTo deploy to experimental hive:" -ForegroundColor Yellow
    Write-Host "  .\install-test.ps1" -ForegroundColor Gray
} else {
    Write-Error "VSIX package was not created at expected path: $vsixPath"
}
