#!/usr/bin/env bash
# Installs DirOpusReImagined to ~/.local and registers the desktop entry.
#
# Layout mirrors HotKeyViewer: the real tree lives in lib/, and bin/ holds a
# symlink. A symlink rather than a copy because the app resolves Configuration.xml
# and Assets/ through AppContext.BaseDirectory, which .NET derives from
# /proc/self/exe — that follows the symlink to the real directory, so the binary
# keeps finding its files no matter where it is invoked from.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFIX="${PREFIX:-$HOME/.local}"
APP="diropus"
INSTALL_DIR="$PREFIX/lib/$APP"
BIN_DIR="$PREFIX/bin"
DESKTOP_DIR="$PREFIX/share/applications"
MODE="${MODE:-self-contained}"
RID="${RID:-linux-x64}"

"$REPO_ROOT/packaging/build.sh" "--$MODE" --rid "$RID" --no-tarball

BUILD_DIR="$REPO_ROOT/dist/$MODE/$RID"

# Files the running app writes into its own directory, and which the build also ships fresh
# copies of. Without carrying them across, every reinstall silently reverts them to stock:
#
#   Configuration.xml  everything the settings dialog saves - panel fonts, titles, start paths,
#                      terminal command, UI scale, and all 36 button definitions. The app resolves
#                      it through AppContext.BaseDirectory, so on an installed copy this IS the
#                      user's settings file, not a template.
#   BOOKMARKS.MD       saved bookmarks (BookmarkStore, same base directory).
#
# Set RESET_CONFIG=1 to deliberately take the shipped defaults instead.
USER_DATA=(Configuration.xml BOOKMARKS.MD)

USER_DATA_STASH=""
if [[ -z ${RESET_CONFIG:-} ]]; then
  USER_DATA_STASH="$(mktemp -d)"
  for f in "${USER_DATA[@]}"; do
    if [[ -s "$INSTALL_DIR/$f" ]]; then
      cp "$INSTALL_DIR/$f" "$USER_DATA_STASH/$f"
    fi
  done
fi

rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$DESKTOP_DIR"

# cp -a, not `find -maxdepth 1`: this build has Assets/ and the runtime's
# satellite-assembly directories, so the tree has to come across whole.
cp -a "$BUILD_DIR/." "$INSTALL_DIR/"
chmod 755 "$INSTALL_DIR/DirOpusReImagined"

# Put the user's own files back over the freshly shipped ones. Ifs, not && chains: under
# `set -e` a false [[ ]] at the head of an && list takes the whole script down.
if [[ -n $USER_DATA_STASH ]]; then
  for f in "${USER_DATA[@]}"; do
    if [[ -f "$USER_DATA_STASH/$f" ]]; then
      cp "$USER_DATA_STASH/$f" "$INSTALL_DIR/$f"
      echo "kept your existing $f"
    fi
  done
  rm -rf "$USER_DATA_STASH"
fi

ln -sfn "$INSTALL_DIR/DirOpusReImagined" "$BIN_DIR/$APP"
install -m 644 "$REPO_ROOT/packaging/$APP.desktop" "$DESKTOP_DIR/$APP.desktop"

command -v update-desktop-database >/dev/null && update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true

echo
echo "Installed $MODE build to $INSTALL_DIR ($(du -sh "$INSTALL_DIR" | cut -f1))"
echo "Run: $APP   (ensure $BIN_DIR is on your PATH)"
if [[ -n ${RESET_CONFIG:-} ]]; then
  echo "RESET_CONFIG was set: Configuration.xml and BOOKMARKS.MD are the shipped defaults."
else
  echo "Configuration.xml and BOOKMARKS.MD are preserved across reinstalls (RESET_CONFIG=1 to reset)."
fi
