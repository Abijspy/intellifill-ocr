#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-6.1.0}"
RID="${2:-osx-arm64}"
CONFIGURATION="${3:-Release}"

[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.][0-9]+)?$ ]] || { echo "Invalid version: $VERSION" >&2; exit 2; }
[[ "$RID" == "osx-arm64" || "$RID" == "osx-x64" ]] || { echo "Runtime must be osx-arm64 or osx-x64." >&2; exit 2; }
command -v dotnet >/dev/null || { echo "The .NET SDK is required." >&2; exit 1; }
command -v hdiutil >/dev/null || { echo "Run this script on macOS; hdiutil was not found." >&2; exit 1; }
command -v iconutil >/dev/null || { echo "Run this script on macOS; iconutil was not found." >&2; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$ROOT/src/IntelliFillOCR.Avalonia/IntelliFillOCR.Avalonia.csproj"
CLI_PROJECT="$ROOT/src/IntelliFillOCR.Cli/IntelliFillOCR.Cli.csproj"
OUT="$ROOT/release/macos"
WORK="$OUT/work-$RID"
PUBLISH="$WORK/publish"
CLI_PUBLISH="$WORK/cli"
APP="$WORK/IntelliFill OCR.app"
CONTENTS="$APP/Contents"
STAGING="$WORK/dmg"
DMG="$OUT/IntelliFillOCR-$VERSION-$RID.dmg"

rm -rf "$WORK"
mkdir -p "$OUT" "$PUBLISH" "$CLI_PUBLISH" "$CONTENTS/MacOS" "$CONTENTS/Resources/cli" "$STAGING"

dotnet publish "$PROJECT" -c "$CONFIGURATION" -r "$RID" --self-contained true \
  -p:IntelliFillPlatform=macOS -p:PublishSingleFile=false -p:PublishDir="$PUBLISH/"
dotnet publish "$CLI_PROJECT" -c "$CONFIGURATION" -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -p:PublishDir="$CLI_PUBLISH/"

cp -a "$PUBLISH/." "$CONTENTS/MacOS/"
cp -a "$CLI_PUBLISH/." "$CONTENTS/Resources/cli/"
chmod 755 "$CONTENTS/MacOS/IntelliFillOCR" "$CONTENTS/Resources/cli/intellifill"

ICONSET="$WORK/IntelliFillOCR.iconset"
mkdir -p "$ICONSET"
for specification in "16 icon_16x16" "32 icon_16x16@2x" "32 icon_32x32" "64 icon_32x32@2x" "128 icon_128x128" "256 icon_128x128@2x" "256 icon_256x256" "512 icon_256x256@2x" "512 icon_512x512" "1024 icon_512x512@2x"; do
  size="${specification%% *}"
  name="${specification#* }"
  sips -z "$size" "$size" "$ROOT/assets/logo_512.png" --out "$ICONSET/$name.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$CONTENTS/Resources/IntelliFillOCR.icns"

cat > "$CONTENTS/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>IntelliFill OCR</string>
  <key>CFBundleDisplayName</key><string>IntelliFill OCR</string>
  <key>CFBundleIdentifier</key><string>com.abishekprabakaran.IntelliFillOCR</string>
  <key>CFBundleExecutable</key><string>IntelliFillOCR</string>
  <key>CFBundleIconFile</key><string>IntelliFillOCR</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Copyright © 2026 Abishek Prabakaran. MIT License.</string>
</dict></plist>
PLIST
plutil -lint "$CONTENTS/Info.plist"

# Use a configured Developer ID when available; otherwise apply an ad-hoc
# signature so bundle integrity can still be checked during CI and local tests.
SIGN_IDENTITY="${MACOS_SIGN_IDENTITY:--}"
codesign --force --deep --options runtime --sign "$SIGN_IDENTITY" "$APP"
codesign --verify --deep --strict "$APP"

cp -a "$APP" "$STAGING/"
ln -s /Applications "$STAGING/Applications"
cat > "$STAGING/CLI Installation.txt" <<'CLI_HELP'
IntelliFill OCR includes the complete intellifill command line.

After dragging the app to Applications, make it available in your shell with:

  sudo ln -sf "/Applications/IntelliFill OCR.app/Contents/Resources/cli/intellifill" /usr/local/bin/intellifill

Then run: intellifill --help
CLI_HELP

rm -f "$DMG"
hdiutil create -volname "IntelliFill OCR $VERSION" -srcfolder "$STAGING" -ov -format UDZO "$DMG"
echo "Created $DMG"
