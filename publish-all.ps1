# Publish single-file, self-contained builds of DirOpusReImagined for every supported platform.
#
# Each platform gets its own folder under publish\<rid>\ containing:
#   - a single-file executable (managed + native libraries bundled inside it)
#   - Configuration.xml         (kept SEPARATE from the exe, platform-appropriate, user-editable)
#   - Assets\                   (icons the app loads from disk at runtime)
# A release .zip per platform is also written to dist\.
#
# Usage:
#   .\publish-all.ps1                      # build all platforms
#   .\publish-all.ps1 -Rids osx-arm64      # build only the listed RID(s)
#   .\publish-all.ps1 -Rids win-x64,win-x86
#
# Cross-publishing works from any OS (the SDK pulls the target runtime pack).

param([string[]]$Rids)

$ErrorActionPreference = "Stop"

$Project = "DirOpusReImagined.csproj"
$Config = "Release"
$OutBase = "publish"
$DistBase = "dist"
$AppName = "DirOpusReImagined"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "'dotnet' not found on PATH"
    exit 1
}

$csprojXml = [xml](Get-Content $Project)
$Version = ($csprojXml.Project.PropertyGroup.AssemblyVersion | Where-Object { $_ } | Select-Object -First 1)
if (-not $Version) {
    Write-Error "Could not determine version from $Project"
    exit 1
}

$Targets = @(
    @{ RID = "win-x64";     Label = "Windows Intel/AMD 64-bit" }
    @{ RID = "win-x86";     Label = "Windows Intel/AMD 32-bit" }
    @{ RID = "win-arm64";   Label = "Windows ARM" }
    @{ RID = "osx-x64";     Label = "macOS Intel" }
    @{ RID = "osx-arm64";   Label = "macOS Apple Silicon" }
    @{ RID = "linux-x64";   Label = "Linux Intel/AMD 64-bit" }
    @{ RID = "linux-arm64"; Label = "Linux ARM" }
)

# Which source config file ships (renamed to Configuration.xml) for a given RID.
function Get-ConfigSource($rid) {
    switch -Wildcard ($rid) {
        "win-*"   { "Configuration.xml" }
        "osx-*"   { "MACConfiguration .xml" }
        "linux-*" { "LINUXConfiguration.xml" }
        default   { "Configuration.xml" }
    }
}

Write-Host "============================================"
Write-Host " DirOpusReImagined v$Version - Multi-Platform Publish"
Write-Host "============================================"
Write-Host ""

New-Item -ItemType Directory -Force -Path $DistBase | Out-Null

foreach ($target in $Targets) {
    $rid = $target.RID
    $label = $target.Label

    # If a subset was requested, skip anything not in it.
    if ($Rids -and ($rid -notin $Rids)) { continue }

    $outDir = "$OutBase/$rid"
    $zipName = "$AppName-$Version-$rid.zip"
    $zipPath = Join-Path $DistBase $zipName

    Write-Host "==> $label ($rid)"

    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

    dotnet publish $Project `
        -c $Config `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -o $outDir

    # Ship the platform-appropriate config as a SEPARATE, editable file beside the exe.
    $srcCfg = Get-ConfigSource $rid
    if (Test-Path -LiteralPath $srcCfg) {
        Copy-Item -LiteralPath $srcCfg -Destination (Join-Path $outDir "Configuration.xml") -Force
    } else {
        Write-Warning "config '$srcCfg' not found; using whatever the build emitted"
    }

    # Tidy up stray artifacts so the folder is just exe + config + Assets.
    Get-ChildItem -Path $outDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force
    $binPath = Join-Path $outDir "bin"
    if (Test-Path $binPath) { Remove-Item -Recurse -Force $binPath }
    $dsStore = Join-Path $outDir ".DS_Store"
    if (Test-Path $dsStore) { Remove-Item -Force $dsStore }

    # Zip the platform folder for release distribution.
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $outDir -DestinationPath $zipPath -CompressionLevel Optimal
    $sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host "   -> $outDir/   (zip: $zipPath, $sizeMB MB)"
    Write-Host ""
}

Write-Host "============================================"
Write-Host " All builds complete."
Write-Host "   Binaries:     $OutBase/<rid>/"
Write-Host "   Release zips: $DistBase/"
Write-Host "============================================"
Write-Host ""
Get-ChildItem "$DistBase/*.zip" | Format-Table Name, Length
