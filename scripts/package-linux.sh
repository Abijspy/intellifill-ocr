#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-3.4.0}"
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

if ! command -v fpm >/dev/null 2>&1; then
  echo "fpm was not found. Install it with: gem install --no-document fpm" >&2
  exit 1
fi

COMMON_FPM_ARGS=(
  -s dir
  -n intellifill-ocr
  -v "$VERSION"
  --license "Proprietary"
  --maintainer "IntelliFill OCR"
  --description "Offline OCR extraction, table filling, SQLite storage, and traceable exports."
  --url "https://github.com/Abijspy/intellifill-ocr"
  -C "$PKG_ROOT"
  usr/share/intellifill-ocr
  usr/bin/intellifill-ocr
  usr/share/applications/intellifill-ocr.desktop
)

fpm "${COMMON_FPM_ARGS[@]}" \
  -t deb \
  -p "$OUT/intellifill-ocr_${VERSION}_amd64.deb" \
  -d libx11-6 \
  -d libice6 \
  -d libsm6 \
  -d libfontconfig1

fpm "${COMMON_FPM_ARGS[@]}" \
  -t rpm \
  -p "$OUT/intellifill-ocr-${VERSION}-1.x86_64.rpm" \
  -d libX11 \
  -d libICE \
  -d libSM \
  -d fontconfig

echo "Linux packages created in $OUT"
