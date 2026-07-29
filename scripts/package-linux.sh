#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-3.8.1}"
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

# The self-contained .NET runtime links to LTTng UST for diagnostics.  Its
# SONAME differs between RPM distribution releases, so letting alien discover
# it as a system dependency produces RPMs that cannot be installed everywhere.
# Bundle the exact libraries the published runtime was linked against instead.
bundle_lttng_runtime() {
  local target_dir="$PKG_ROOT/usr/share/intellifill-ocr"
  local library source dependency
  local -a libraries

  mapfile -t libraries < <(
    while IFS= read -r -d '' file; do
      readelf -d "$file" 2>/dev/null || true
    done < <(find "$PUBLISH" -type f -print0) |
      awk '/Shared library: \[liblttng-ust/ { gsub(/[\[\]]/, "", $NF); print $NF }' |
      sort -u
  )

  for ((index = 0; index < ${#libraries[@]}; index++)); do
    library="${libraries[index]}"
    source="$(ldconfig -p | awk -v library="$library" '$1 == library { print $NF; exit }')"
    if [ -z "$source" ] || [ ! -e "$source" ]; then
      echo "Required runtime library $library was not found on the packaging host." >&2
      exit 1
    fi

    # Store the file under the SONAME requested by libcoreclr.  This avoids
    # copying host-specific versioned filenames or dangling symlinks.
    install -m 755 "$(readlink -f "$source")" "$target_dir/$library"

    # LTTng UST itself depends on its matching common library.  Include the
    # complete LTTng UST closure, but leave standard system libraries alone.
    while IFS= read -r dependency; do
      if [[ ! " ${libraries[*]} " =~ " $dependency " ]]; then
        libraries+=("$dependency")
      fi
    done < <(
      readelf -d "$(readlink -f "$source")" |
        awk '/Shared library: \[liblttng-ust/ { gsub(/[\[\]]/, "", $NF); print $NF }'
    )
  done
}

bundle_lttng_runtime

cat > "$PKG_ROOT/usr/bin/intellifill-ocr" <<'WRAPPER'
#!/usr/bin/env bash
export LD_LIBRARY_PATH="/usr/share/intellifill-ocr${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
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
