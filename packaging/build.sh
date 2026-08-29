#!/usr/bin/env bash
# Builds a self-contained DirOpusReImagined that runs on a machine with no .NET installed.
#
# Two modes:
#
#   --self-contained (default)  A directory: apphost + the runtime's assemblies.
#                               Nothing to unpack at startup, so it launches
#                               immediately. ~110MB on disk.
#   --single-file               One compressed executable beside its config and
#                               Assets. Smaller, but the runtime unpacks itself
#                               to a temp dir on first launch.
#
# NativeAOT (what HotKeyViewer uses) is deliberately not offered: this project
# references Microsoft.CodeAnalysis.CSharp and leans on Avalonia's reflection-based
# XAML loader, neither of which survives AOT's trimming without work.
#
# The app looks for Configuration.xml and Assets/ in the working directory first
# and then next to the executable, so both must ship alongside the binary. The
# build emits the *Windows* Configuration.xml, so we overwrite it with the Linux
# one here — without this the app starts with Windows paths and commands.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/DirOpusReImagined.csproj"
MODE="self-contained"
RID="linux-x64"
TARBALL=1

usage() {
  cat <<'USAGE'
Usage: packaging/build.sh [--self-contained|--single-file] [--rid <rid>] [--no-tarball]

  --self-contained  Directory deployment (default). Fast startup.
  --single-file     One self-extracting executable. Smaller, slower first launch.
  --rid <rid>       Target runtime identifier (default: linux-x64).
  --no-tarball      Skip creating the .tar.gz.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --self-contained) MODE="self-contained"; shift ;;
    --single-file) MODE="single-file"; shift ;;
    --rid) RID="$2"; shift 2 ;;
    --no-tarball) TARBALL=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 1 ;;
  esac
done

command -v dotnet >/dev/null || { echo "error: 'dotnet' not found on PATH" >&2; exit 1; }

OUT="$REPO_ROOT/dist/$MODE/$RID"
rm -rf "$OUT"
mkdir -p "$OUT"

echo "Building $MODE for $RID…"

COMMON=(
  --configuration Release
  --runtime "$RID"
  --self-contained true
  -p:DebugType=none
  -p:DebugSymbols=false
  --output "$OUT"
  --nologo
)

if [[ $MODE == "single-file" ]]; then
  dotnet publish "$PROJECT" "${COMMON[@]}" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true
else
  dotnet publish "$PROJECT" "${COMMON[@]}"
fi

# Ship the platform-appropriate config. The csproj copies the Windows one.
LINUX_CONFIG="$REPO_ROOT/LINUXConfiguration.xml"
if [[ $RID == linux-* ]]; then
  if [[ -f $LINUX_CONFIG ]]; then
    cp "$LINUX_CONFIG" "$OUT/Configuration.xml"
  else
    echo "warning: $LINUX_CONFIG missing; shipping the config the build emitted" >&2
  fi
fi

# The app reads Assets/ from beside the binary; fail loudly rather than shipping
# a build that comes up with missing toolbar icons.
[[ -d $OUT/Assets ]] || { echo "error: Assets/ missing from $OUT" >&2; exit 1; }

rm -f "$OUT"/*.pdb

echo
echo "Output: $OUT  ($(du -sh "$OUT" | cut -f1))"

if [[ $TARBALL -eq 1 ]]; then
  mkdir -p "$REPO_ROOT/dist"
  ARCHIVE="$REPO_ROOT/dist/diropusreimagined-$RID-$MODE.tar.gz"
  tar -czf "$ARCHIVE" -C "$OUT" .
  echo "Archive: $ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"
fi
