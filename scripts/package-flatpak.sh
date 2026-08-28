#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-6.2.0}"
RID="${2:-linux-x64}"
CONFIGURATION="${3:-Release}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
APP_ID="com.abishekprabakaran.IntelliFillOCR"
OUT="$ROOT/release/flatpak"
STAGING="$OUT/staging"
PUBLISH="$STAGING/publish"
BUILD="$OUT/build-$RID"
REPOSITORY="$OUT/repository-$RID"
MANIFEST="$ROOT/packaging/flatpak/$APP_ID.yml"

case "$RID" in
  linux-x64) expected_arch=x86_64 ;;
  linux-arm64) expected_arch=aarch64 ;;
  *) echo "Unsupported Flatpak runtime: $RID" >&2; exit 2 ;;
esac

actual_arch="$(uname -m)"
if [ "$actual_arch" != "$expected_arch" ]; then
  echo "Flatpak $RID must be built natively on $expected_arch (current machine: $actual_arch)." >&2
  exit 2
fi

for tool in dotnet flatpak flatpak-builder python3; do
  command -v "$tool" >/dev/null || { echo "$tool is required to build the Flatpak." >&2; exit 1; }
done

rm -rf "$STAGING" "$BUILD" "$REPOSITORY"
mkdir -p "$PUBLISH"

dotnet publish "$ROOT/src/IntelliFillOCR.Avalonia/IntelliFillOCR.Avalonia.csproj" \
  -c "$CONFIGURATION" -r "$RID" --self-contained true \
  -p:IntelliFillPlatform=Linux -p:PublishSingleFile=false -p:PublishDir="$PUBLISH/"
dotnet publish "$ROOT/src/IntelliFillOCR.Cli/IntelliFillOCR.Cli.csproj" \
  -c "$CONFIGURATION" -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -p:PublishDir="$PUBLISH/cli/"

rm -f "$PUBLISH/libcoreclrtraceptprovider.so" "$PUBLISH/cli/libcoreclrtraceptprovider.so"

cat > "$STAGING/intellifill-ocr" <<'EOF'
#!/bin/sh
export TESSDATA_PREFIX=/app/share/tessdata
export PATH=/app/bin:$PATH
exec /app/lib/intellifill/IntelliFillOCR "$@"
EOF
cat > "$STAGING/intellifill" <<'EOF'
#!/bin/sh
export TESSDATA_PREFIX=/app/share/tessdata
export PATH=/app/bin:$PATH
exec /app/lib/intellifill/cli/intellifill "$@"
EOF
chmod 755 "$STAGING/intellifill-ocr" "$STAGING/intellifill"

cat > "$STAGING/$APP_ID.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=IntelliFill OCR
Comment=Offline OCR extraction and table filling
Exec=intellifill-ocr
Icon=$APP_ID
Terminal=false
Categories=Office;
StartupNotify=true
StartupWMClass=IntelliFillOCR
EOF

python3 "$ROOT/scripts/generate-linux-metadata.py" \
  "$VERSION" \
  "$ROOT/src/IntelliFillOCR.Avalonia/MainWindow.axaml.cs" \
  "$ROOT/packaging/linux/$APP_ID.metainfo.xml.in" \
  "$ROOT/packaging/linux/changelog.in" \
  "$STAGING/$APP_ID.metainfo.xml" \
  "$STAGING/changelog"
sed -i "s#<launchable type=\"desktop-id\">intellifill-ocr.desktop</launchable>#<launchable type=\"desktop-id\">$APP_ID.desktop</launchable>#" "$STAGING/$APP_ID.metainfo.xml"
sed -i "s#<icon type=\"stock\">intellifill-ocr</icon>#<icon type=\"stock\">$APP_ID</icon>#" "$STAGING/$APP_ID.metainfo.xml"
install -m 0644 "$ROOT/assets/logo_512.png" "$STAGING/$APP_ID.png"

flatpak-builder --force-clean --repo="$REPOSITORY" "$BUILD" "$MANIFEST"
flatpak build-bundle "$REPOSITORY" "$OUT/IntelliFillOCR-$VERSION-$RID.flatpak" "$APP_ID" stable

echo "Flatpak bundle created: $OUT/IntelliFillOCR-$VERSION-$RID.flatpak"
