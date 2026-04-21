#!/bin/bash
# Build single-file executables for all supported platforms
# Usage: ./publish-all.sh
# Output: publish/<platform>/ directories

set -e

PROJECT="DirOpusReImagined.csproj"
CONFIG="Release"
OUT_BASE="publish"
DIST_BASE="dist"

VERSION=$(grep -oE '<AssemblyVersion>[^<]+' "${PROJECT}" | head -1 | sed 's/<AssemblyVersion>//')
if [ -z "${VERSION}" ]; then
    echo "ERROR: Could not determine version from ${PROJECT}" >&2
    exit 1
fi
APP_NAME="DirOpusReImagined"

TARGETS=(
    "win-x64:Windows Intel/AMD 64-bit"
    "win-x86:Windows Intel/AMD 32-bit"
    "win-arm64:Windows ARM"
    "osx-x64:macOS Intel"
    "osx-arm64:macOS Apple Silicon"
    "linux-x64:Linux Intel/AMD"
    "linux-arm64:Linux ARM"
)

echo "============================================"
echo " DirOpusReImagined v${VERSION} - Multi-Platform Publish"
echo "============================================"
echo ""

mkdir -p "${DIST_BASE}"

for entry in "${TARGETS[@]}"; do
    RID="${entry%%:*}"
    LABEL="${entry##*:}"
    OUTDIR="${OUT_BASE}/${RID}"
    ZIP_NAME="${APP_NAME}-${VERSION}-${RID}.zip"
    ZIP_PATH="${DIST_BASE}/${ZIP_NAME}"

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

    rm -f "${ZIP_PATH}"
    (cd "${OUT_BASE}" && zip -rq "../${ZIP_PATH}" "${RID}")
    echo "  -> ${ZIP_PATH} ($(du -h "${ZIP_PATH}" | cut -f1))"
    echo ""
done

echo "============================================"
echo " All builds complete."
echo "   Binaries: ${OUT_BASE}/"
echo "   Release zips: ${DIST_BASE}/"
echo "============================================"
echo ""
ls -lh "${DIST_BASE}"/*.zip
