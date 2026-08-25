#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-5.1.0}"
RID="${2:-linux-x64}"
CONFIGURATION="${3:-Release}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$ROOT/src/IntelliFillOCR.Avalonia/IntelliFillOCR.Avalonia.csproj"
CLI_PROJECT="$ROOT/src/IntelliFillOCR.Cli/IntelliFillOCR.Cli.csproj"
OUT="$ROOT/release/linux"
PUBLISH="$ROOT/release/avalonia-$RID/publish"
CLI_PUBLISH="$PUBLISH/cli"

mkdir -p "$OUT" "$PUBLISH"

dotnet publish "$PROJECT" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  -p:IntelliFillPlatform=Linux \
  -p:PublishSingleFile=false \
  -p:PublishDir="$PUBLISH/"

dotnet publish "$CLI_PROJECT" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishDir="$CLI_PUBLISH/"

cat > "$PUBLISH/intellifill" <<'CLI_WRAPPER'
#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$SCRIPT_DIR/cli/intellifill" "$@"
CLI_WRAPPER
chmod 755 "$PUBLISH/intellifill"
install -d -m 0755 "$PUBLISH/repositories"
install -m 0755 "$ROOT/scripts/install-linux-repository.sh" "$PUBLISH/repositories/install-linux-repository.sh"

# This optional .NET diagnostic provider is linked to the retired
# liblttng-ust.so.0 ABI. It is not used by IntelliFill OCR at runtime, but its
# presence makes RPM package managers require that unavailable library on
# current Fedora releases.
rm -f "$PUBLISH/libcoreclrtraceptprovider.so" "$CLI_PUBLISH/libcoreclrtraceptprovider.so"

TAR="$OUT/IntelliFillOCR-$VERSION-$RID.tar.gz"
tar -C "$PUBLISH" -czf "$TAR" .

PKG_ROOT="$OUT/pkgroot"
rm -rf "$PKG_ROOT"
mkdir -p \
  "$PKG_ROOT/usr/share/intellifill-ocr" \
  "$PKG_ROOT/usr/share/intellifill-ocr/repositories" \
  "$PKG_ROOT/usr/share/keyrings" \
  "$PKG_ROOT/usr/bin" \
  "$PKG_ROOT/usr/share/applications" \
  "$PKG_ROOT/usr/share/icons/hicolor/512x512/apps" \
  "$PKG_ROOT/usr/share/pixmaps"
cp "$ROOT/package-repository/keys/intellifill-ocr-archive-keyring.gpg" "$PKG_ROOT/usr/share/keyrings/intellifill-ocr-archive-keyring.gpg"
cat > "$PKG_ROOT/usr/share/intellifill-ocr/repositories/intellifill-ocr.repo" <<'REPO'
[intellifill-ocr]
name=IntelliFill OCR
baseurl=https://packages.abishekprabakaran.com/rpm/$basearch
enabled=1
gpgcheck=0
repo_gpgcheck=1
gpgkey=https://packages.abishekprabakaran.com/keys/intellifill-ocr-archive-keyring.gpg
REPO
cp -a "$PUBLISH/." "$PKG_ROOT/usr/share/intellifill-ocr/"
cat > "$PKG_ROOT/usr/bin/intellifill-ocr" <<'WRAPPER'
#!/usr/bin/env bash
exec /usr/share/intellifill-ocr/IntelliFillOCR "$@"
WRAPPER
chmod 755 "$PKG_ROOT/usr/bin/intellifill-ocr"
cat > "$PKG_ROOT/usr/bin/intellifill" <<'CLI_WRAPPER'
#!/usr/bin/env bash
exec /usr/share/intellifill-ocr/cli/intellifill "$@"
CLI_WRAPPER
chmod 755 "$PKG_ROOT/usr/bin/intellifill"
install -m 644 "$ROOT/assets/logo_512.png" "$PKG_ROOT/usr/share/icons/hicolor/512x512/apps/intellifill-ocr.png"
install -m 644 "$ROOT/assets/logo_512.png" "$PKG_ROOT/usr/share/pixmaps/intellifill-ocr.png"
cat > "$PKG_ROOT/usr/share/applications/intellifill-ocr.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=IntelliFill OCR
Comment=Offline OCR extraction and table filling
Exec=/usr/bin/intellifill-ocr
Icon=intellifill-ocr
Terminal=false
Categories=Office;
StartupNotify=true
StartupWMClass=IntelliFillOCR
DESKTOP

mkdir -p "$PKG_ROOT/DEBIAN"
cat > "$PKG_ROOT/DEBIAN/postinst" <<'POSTINST'
#!/bin/sh
set -e
bash /usr/share/intellifill-ocr/repositories/install-linux-repository.sh || true
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi
POSTINST
chmod 755 "$PKG_ROOT/DEBIAN/postinst"
cat > "$PKG_ROOT/DEBIAN/postrm" <<'POSTRM'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi
POSTRM
chmod 755 "$PKG_ROOT/DEBIAN/postrm"
cat > "$PKG_ROOT/DEBIAN/control" <<CONTROL
Package: intellifill-ocr
Version: $VERSION
Section: utils
Priority: optional
Architecture: amd64
Maintainer: IntelliFill OCR
Depends: libx11-6, libice6, libsm6, libfontconfig1
Description: Offline OCR extraction, table filling, SQLite storage, and traceable exports.
CONTROL

DEB="$OUT/intellifill-ocr_${VERSION}_amd64.deb"
dpkg-deb --build "$PKG_ROOT" "$DEB"

if ! command -v alien >/dev/null 2>&1; then
  echo "alien was not found. Install it to convert the Debian package to RPM." >&2
  exit 1
fi

(
  cd "$OUT"
  alien --to-rpm --keep-version "$(basename "$DEB")"
)

echo "Linux packages created in $OUT"
