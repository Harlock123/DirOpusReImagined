# Build single-file executables for all supported platforms
# Usage: .\publish-all.ps1
# Output: publish\<platform>\ directories

$ErrorActionPreference = "Stop"

$Project = "DirOpusReImagined.csproj"
$Config = "Release"
$OutBase = "publish"
$DistBase = "dist"
$AppName = "DirOpusReImagined"

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
    @{ RID = "linux-x64";   Label = "Linux Intel/AMD" }
    @{ RID = "linux-arm64"; Label = "Linux ARM" }
)

Write-Host "============================================"
Write-Host " DirOpusReImagined v$Version - Multi-Platform Publish"
Write-Host "============================================"
Write-Host ""

New-Item -ItemType Directory -Force -Path $DistBase | Out-Null

foreach ($target in $Targets) {
    $rid = $target.RID
    $label = $target.Label
    $outDir = "$OutBase/$rid"
    $zipName = "$AppName-$Version-$rid.zip"
    $zipPath = Join-Path $DistBase $zipName

    Write-Host "Building $label ($rid)..."

    dotnet publish $Project `
        -c $Config `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $outDir

    Write-Host "  -> $outDir/"

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $outDir -DestinationPath $zipPath -CompressionLevel Optimal
    $sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host "  -> $zipPath ($sizeMB MB)"
    Write-Host ""
}

Write-Host "============================================"
Write-Host " All builds complete."
Write-Host "   Binaries: $OutBase/"
Write-Host "   Release zips: $DistBase/"
Write-Host "============================================"
Write-Host ""
Get-ChildItem "$DistBase/*.zip" | Format-Table Name, Length
