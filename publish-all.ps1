# Build single-file executables for all supported platforms
# Usage: .\publish-all.ps1
# Output: publish\<platform>\ directories

$ErrorActionPreference = "Stop"

$Project = "DirOpusReImagined.csproj"
$Config = "Release"
$OutBase = "publish"

$Targets = @(
    @{ RID = "win-x64";     Label = "Windows Intel/AMD" }
    @{ RID = "win-arm64";   Label = "Windows ARM" }
    @{ RID = "osx-x64";     Label = "macOS Intel" }
    @{ RID = "osx-arm64";   Label = "macOS Apple Silicon" }
    @{ RID = "linux-x64";   Label = "Linux Intel/AMD" }
    @{ RID = "linux-arm64"; Label = "Linux ARM" }
)

Write-Host "============================================"
Write-Host " DirOpusReImagined - Multi-Platform Publish"
Write-Host "============================================"
Write-Host ""

foreach ($target in $Targets) {
    $rid = $target.RID
    $label = $target.Label
    $outDir = "$OutBase/$rid"

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
    Write-Host ""
}

Write-Host "============================================"
Write-Host " All builds complete. Output in $OutBase/"
Write-Host "============================================"
Write-Host ""
Get-ChildItem -Directory $OutBase
