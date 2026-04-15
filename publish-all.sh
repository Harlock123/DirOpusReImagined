#!/bin/bash
# Build single-file executables for all supported platforms
# Usage: ./publish-all.sh
# Output: publish/<platform>/ directories

set -e

PROJECT="DirOpusReImagined.csproj"
CONFIG="Release"
OUT_BASE="publish"

TARGETS=(
    "win-x64:Windows Intel/AMD"
    "win-arm64:Windows ARM"
    "osx-x64:macOS Intel"
    "osx-arm64:macOS Apple Silicon"
    "linux-x64:Linux Intel/AMD"
    "linux-arm64:Linux ARM"
)

echo "============================================"
echo " DirOpusReImagined - Multi-Platform Publish"
echo "============================================"
echo ""

for entry in "${TARGETS[@]}"; do
    RID="${entry%%:*}"
    LABEL="${entry##*:}"
    OUTDIR="${OUT_BASE}/${RID}"

    echo "Building ${LABEL} (${RID})..."

    dotnet publish "${PROJECT}" \
        -c "${CONFIG}" \
        -r "${RID}" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -o "${OUTDIR}"

    echo "  -> ${OUTDIR}/"
    echo ""
done

echo "============================================"
echo " All builds complete. Output in ${OUT_BASE}/"
echo "============================================"
echo ""
ls -d ${OUT_BASE}/*/
