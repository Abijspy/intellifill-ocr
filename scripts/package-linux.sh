#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-3.7.3}"
RID="${2:-linux-x64}"
CONFIGURATION="${3:-Release}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$ROOT/src/IntelliFillOCR.Avalonia/IntelliFillOCR.Avalonia.csproj"
OUT="$ROOT/release/linux"
PUBLISH="$ROOT/release/avalonia-$RID/publish"

mkdir -p "$OUT" "$PUBLISH"

dotnet publish "$PROJECT" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishDir="$PUBLISH/"

TAR="$OUT/IntelliFillOCR-$VERSION-$RID.tar.gz"
tar -C "$PUBLISH" -czf "$TAR" .

PKG_ROOT="$OUT/pkgroot"
rm -rf "$PKG_ROOT"
mkdir -p "$PKG_ROOT/usr/share/intellifill-ocr" "$PKG_ROOT/usr/bin" "$PKG_ROOT/usr/share/applications"
cp -a "$PUBLISH/." "$PKG_ROOT/usr/share/intellifill-ocr/"
cat > "$PKG_ROOT/usr/bin/intellifill-ocr" <<'WRAPPER'
#!/usr/bin/env bash
exec /usr/share/intellifill-ocr/IntelliFillOCR "$@"
WRAPPER
chmod 755 "$PKG_ROOT/usr/bin/intellifill-ocr"
cat > "$PKG_ROOT/usr/share/applications/intellifill-ocr.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=IntelliFill OCR
Comment=Offline OCR extraction and table filling
Exec=/usr/bin/intellifill-ocr
Terminal=false
Categories=Office;Utility;
DESKTOP

mkdir -p "$PKG_ROOT/DEBIAN"
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
