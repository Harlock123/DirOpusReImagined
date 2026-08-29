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

# BOOKMARKS.MD is written next to the binary (BookmarkStore uses AppContext.BaseDirectory)
# AND is shipped by the build as an empty stub, so a reinstall would overwrite the
# user's bookmarks. Stash them and put them back after the copy.
BOOKMARKS_BACKUP=""
if [[ -s "$INSTALL_DIR/BOOKMARKS.MD" ]]; then
  BOOKMARKS_BACKUP="$(mktemp)"
  cp "$INSTALL_DIR/BOOKMARKS.MD" "$BOOKMARKS_BACKUP"
fi

rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$DESKTOP_DIR"

# cp -a, not `find -maxdepth 1`: this build has Assets/ and the runtime's
# satellite-assembly directories, so the tree has to come across whole.
cp -a "$BUILD_DIR/." "$INSTALL_DIR/"
chmod 755 "$INSTALL_DIR/DirOpusReImagined"

# An if, not an && chain: under `set -e` a false [[ ]] at the head of an && list
# takes the whole script down.
if [[ -n $BOOKMARKS_BACKUP ]]; then
  cp "$BOOKMARKS_BACKUP" "$INSTALL_DIR/BOOKMARKS.MD"
  rm -f "$BOOKMARKS_BACKUP"
fi

ln -sfn "$INSTALL_DIR/DirOpusReImagined" "$BIN_DIR/$APP"
install -m 644 "$REPO_ROOT/packaging/$APP.desktop" "$DESKTOP_DIR/$APP.desktop"

command -v update-desktop-database >/dev/null && update-desktop-database "$DESKTOP_DIR" 2>/dev/null || true

echo
echo "Installed $MODE build to $INSTALL_DIR ($(du -sh "$INSTALL_DIR" | cut -f1))"
echo "Run: $APP   (ensure $BIN_DIR is on your PATH)"
