<#
.SYNOPSIS
    Packages SharpClaw (Uno desktop + API backend) as an MSIX for
    Microsoft Store submission via Partner Center.

.DESCRIPTION
    1. Publishes the Uno app + bundled backend (same as publish-release.ps1)
    2. Strips foreign-platform native libraries
    3. Generates the MSIX AppxManifest.xml from your Partner Center identity
    4. Packages everything into a .msix using makeappx.exe

    For Store submission the MSIX does NOT need to be signed - Partner Center
    signs it with the Store certificate automatically.

    Before first use, you must fill in the three identity values from Partner
    Center → Product identity page:
      -IdentityName    e.g. "12345Publisher.SharpClaw"
      -IdentityPublisher  e.g. "CN=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
      -PublisherDisplayName  e.g. "marko"

.PARAMETER Version
    Package version in Major.Minor.Build.0 format. The last quad MUST be 0
    for Store packages. Default: 0.0.1.0

.PARAMETER IdentityName
    From Partner Center → Product identity → Package/Identity/Name.

.PARAMETER IdentityPublisher
    From Partner Center → Product identity → Package/Identity/Publisher.

.PARAMETER PublisherDisplayName
    From Partner Center → Product identity → Package/Properties/PublisherDisplayName.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER OutputDir
    Final output directory. Default: publish\ at repo root.

.PARAMETER Rid
    Runtime identifier. Default: win-x64.

.EXAMPLE
    .\publish-msix.ps1 `
        -IdentityName "12345Publisher.SharpClaw" `
        -IdentityPublisher "CN=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" `
        -PublisherDisplayName "marko" `
        -Version "0.0.1.0"

.EXAMPLE
    # Quick local test (uses placeholder identity - won't pass Store validation)
    .\publish-msix.ps1 -Version "0.0.1.0"
#>
param(
    [string]$Version = "0.0.1.0",
    [string]$IdentityName = "mkn8rn.SharpClaw",
    [string]$IdentityPublisher = "CN=mkn8rn",
    [string]$PublisherDisplayName = "marko",
    [string]$Configuration = "Release",
    [string]$OutputDir = (Join-Path $PSScriptRoot "publish"),
    [string]$Rid = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# -- Validate version format (must end in .0 for Store) --
if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') {
    Write-Error "Version must be in Major.Minor.Build.0 format (last quad must be 0 for Store). Got: $Version"
    exit 1
}

# -- Locate tools --
$sdkToolsBase = "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages\microsoft.windows.sdk.buildtools"
$makeappx = Get-ChildItem $sdkToolsBase -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "x64" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $makeappx) {
    # Fallback: search Windows SDK
    $makeappx = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "x64" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $makeappx) {
    Write-Error "makeappx.exe not found. Install the Windows SDK or ensure microsoft.windows.sdk.buildtools NuGet is present."
    exit 1
}

Write-Host "Using makeappx: $makeappx" -ForegroundColor DarkGray

# -- Paths --
$repoRoot   = $PSScriptRoot
$unoProject = Join-Path (Join-Path $repoRoot "SharpClaw.Uno") "SharpClaw.Uno.csproj"
$stageDir   = Join-Path $OutputDir "SharpClaw-msix-stage"
$msixPath   = Join-Path $OutputDir "SharpClaw-$Rid.msix"
$tfm        = "net10.0-desktop"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SharpClaw MSIX Build" -ForegroundColor Cyan
Write-Host "  Version:   $Version" -ForegroundColor Cyan
Write-Host "  RID:       $Rid" -ForegroundColor Cyan
Write-Host "  Identity:  $IdentityName" -ForegroundColor Cyan
Write-Host "  Publisher: $IdentityPublisher" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# -- Clean --
if (Test-Path $stageDir)  { Remove-Item $stageDir  -Recurse -Force }
if (Test-Path $msixPath)  { Remove-Item $msixPath  -Force }

# -- Publish app + backend --
Write-Host "Publishing Uno app + bundled backend ..." -ForegroundColor Cyan

$publishArgs = @(
    "publish", $unoProject,
    "-c", $Configuration,
    "-f", $tfm,
    "-r", $Rid,
    "--self-contained",
    "-p:BundleBackend=true",
    "-p:UseMonoRuntime=false",
    "-p:PublishReadyToRun=true",
    "-o", $stageDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit 1
}

# -- Strip foreign-platform natives (same logic as publish-release.ps1) --
Write-Host "Stripping foreign-platform natives ..." -ForegroundColor Cyan

$ridArch = ($Rid -split '-')[-1]
$ridOs   = ($Rid -split '-')[0]

foreach ($runtimesDir in (Get-ChildItem $stageDir -Recurse -Directory -Filter "runtimes" -ErrorAction SilentlyContinue)) {
    foreach ($ridDir in (Get-ChildItem $runtimesDir.FullName -Directory -ErrorAction SilentlyContinue)) {
        if ($ridDir.Name -notlike "$ridOs-*" -and $ridDir.Name -ne $ridOs) {
            Remove-Item $ridDir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

$vlcDir = Join-Path $stageDir "libvlc"
if (Test-Path $vlcDir) {
    foreach ($archDir in (Get-ChildItem $vlcDir -Directory -ErrorAction SilentlyContinue)) {
        if ($archDir.Name -notlike "*$ridArch*") {
            Remove-Item $archDir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# -- Remove PDB files to reduce size --
$pdbCount = (Get-ChildItem $stageDir -Recurse -Filter "*.pdb" -ErrorAction SilentlyContinue).Count
Get-ChildItem $stageDir -Recurse -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force
Write-Host "  Removed $pdbCount PDB files" -ForegroundColor DarkGray

# -- Generate AppxManifest.xml --
Write-Host "Generating AppxManifest.xml ..." -ForegroundColor Cyan

# Map RID to MSIX architecture
$msixArch = switch -Wildcard ($Rid) {
    "*-x64"   { "x64" }
    "*-x86"   { "x86" }
    "*-arm64" { "arm64" }
    default   { "x64" }
}

# Resolve icon - copy into package at Assets/
$assetsDir = Join-Path $stageDir "Assets"
if (-not (Test-Path $assetsDir)) { New-Item $assetsDir -ItemType Directory -Force | Out-Null }

$iconSrc = Join-Path $repoRoot "SharpClaw.Uno\Environment\icon.png"
$iconDest = Join-Path $assetsDir "icon.png"
if (Test-Path $iconSrc) {
    Copy-Item $iconSrc $iconDest -Force
}

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">

  <Identity
    Name="$IdentityName"
    Version="$Version"
    Publisher="$IdentityPublisher"
    ProcessorArchitecture="$msixArch" />

  <Properties>
    <DisplayName>SharpClaw</DisplayName>
    <PublisherDisplayName>$PublisherDisplayName</PublisherDisplayName>
    <Logo>Assets\icon.png</Logo>
    <Description>SharpClaw - AI agent orchestrator with local model inference, chat, transcription, and developer tool integration.</Description>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-us" />
  </Resources>

  <Applications>
    <Application Id="SharpClaw"
      Executable="SharpClaw.Uno.exe"
      EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="SharpClaw"
        Description="AI agent orchestrator"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\icon.png"
        Square44x44Logo="Assets\icon.png" />
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@

$manifestPath = Join-Path $stageDir "AppxManifest.xml"
[System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.Encoding]::UTF8)

# -- Remove the template manifest (not the real one) --
$templateManifest = Join-Path $stageDir "Package.appxmanifest"
if (Test-Path $templateManifest) { Remove-Item $templateManifest -Force }

# -- Build MSIX --
Write-Host "Packaging MSIX ..." -ForegroundColor Cyan

& $makeappx pack /d $stageDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) {
    Write-Error "makeappx pack failed with exit code $LASTEXITCODE"
    exit 1
}

# -- Summary --
$msixSize = [math]::Round((Get-Item $msixPath).Length / 1MB, 1)
$stageSize = [math]::Round(
    ((Get-ChildItem $stageDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB), 1
)

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  MSIX Build Complete" -ForegroundColor Green
Write-Host "  Stage size:   $stageSize MB" -ForegroundColor White
Write-Host "  MSIX size:    $msixSize MB" -ForegroundColor White
Write-Host "  Output:       $msixPath" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
