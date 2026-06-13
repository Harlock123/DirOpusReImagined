#!/bin/bash
# Publish single-file, self-contained builds of DirOpusReImagined for every supported platform.
#
# Each platform gets its own folder under publish/<rid>/ containing:
#   - a single-file executable (managed + native libraries bundled inside it)
#   - Configuration.xml         (kept SEPARATE from the exe, platform-appropriate, user-editable)
#   - Assets/                   (icons the app loads from disk at runtime)
# A release .zip per platform is also written to dist/.
#
# Usage:
#   ./publish-all.sh                 # build all platforms
#   ./publish-all.sh osx-arm64       # build only the listed RID(s)
#   ./publish-all.sh win-x64 win-x86
#
# Cross-publishing works from any OS (the SDK pulls the target runtime pack), so this can build
# the Windows and Linux outputs from macOS, etc.

set -e

PROJECT="DirOpusReImagined.csproj"
CONFIG="Release"
OUT_BASE="publish"
DIST_BASE="dist"
APP_NAME="DirOpusReImagined"

command -v dotnet >/dev/null 2>&1 || { echo "ERROR: 'dotnet' not found on PATH" >&2; exit 1; }

VERSION=$(grep -oE '<AssemblyVersion>[^<]+' "${PROJECT}" | head -1 | sed 's/<AssemblyVersion>//')
if [ -z "${VERSION}" ]; then
    echo "ERROR: Could not determine version from ${PROJECT}" >&2
    exit 1
fi

# RID : human-readable label
TARGETS=(
    "win-x64:Windows Intel/AMD 64-bit"
    "win-x86:Windows Intel/AMD 32-bit"
    "win-arm64:Windows ARM"
    "osx-x64:macOS Intel"
    "osx-arm64:macOS Apple Silicon"
    "linux-x64:Linux Intel/AMD 64-bit"
    "linux-arm64:Linux ARM"
)

# Which source config file ships (renamed to Configuration.xml) for a given RID.
config_for() {
    case "$1" in
        win-*)   echo "Configuration.xml" ;;
        osx-*)   echo "MACConfiguration .xml" ;;
        linux-*) echo "LINUXConfiguration.xml" ;;
        *)       echo "Configuration.xml" ;;
    esac
}

# Optional subset of RIDs from the command line (empty = build everything).
SELECT="$*"

build_one() {
    rid="$1"
    label="$2"
    outdir="${OUT_BASE}/${rid}"
    zip_path="${DIST_BASE}/${APP_NAME}-${VERSION}-${rid}.zip"

    echo "==> ${label} (${rid})"

    rm -rf "${outdir}"

    dotnet publish "${PROJECT}" \
        -c "${CONFIG}" \
        -r "${rid}" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=none \
        -p:DebugSymbols=false \
        -o "${outdir}"

    # Ship the platform-appropriate config as a SEPARATE, editable file beside the exe.
    src_cfg="$(config_for "${rid}")"
    if [ -f "${src_cfg}" ]; then
        cp "${src_cfg}" "${outdir}/Configuration.xml"
    else
        echo "   WARNING: config '${src_cfg}' not found; using whatever the build emitted" >&2
    fi

    # Tidy up stray artifacts so the folder is just exe + config + Assets.
    rm -f "${outdir}"/*.pdb "${outdir}/.DS_Store"
    rm -rf "${outdir}/bin"

    # Zip the platform folder for release distribution.
    mkdir -p "${DIST_BASE}"
    rm -f "${zip_path}"
    ( cd "${OUT_BASE}" && zip -rq "../${zip_path}" "${rid}" )
    echo "   -> ${outdir}/   (zip: ${zip_path}, $(du -h "${zip_path}" | cut -f1))"
    echo ""
}

echo "============================================"
echo " DirOpusReImagined v${VERSION} - Multi-Platform Publish"
echo "============================================"
echo ""

for entry in "${TARGETS[@]}"; do
    rid="${entry%%:*}"
    label="${entry##*:}"

    # If a subset was requested, skip anything not in it.
    if [ -n "${SELECT}" ]; then
        case " ${SELECT} " in
            *" ${rid} "*) ;;   # selected
            *) continue ;;      # not selected
        esac
    fi

    build_one "${rid}" "${label}"
done

echo "============================================"
echo " All builds complete."
echo "   Binaries:     ${OUT_BASE}/<rid>/"
echo "   Release zips: ${DIST_BASE}/"
echo "============================================"
echo ""
ls -lh "${DIST_BASE}"/*.zip 2>/dev/null || true
