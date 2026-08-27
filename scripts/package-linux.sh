#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-6.1.0}"
RID="${2:-linux-x64}"
CONFIGURATION="${3:-Release}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PROJECT="$ROOT/src/IntelliFillOCR.Avalonia/IntelliFillOCR.Avalonia.csproj"
CLI_PROJECT="$ROOT/src/IntelliFillOCR.Cli/IntelliFillOCR.Cli.csproj"
OUT="$ROOT/release/linux"
PUBLISH="$ROOT/release/avalonia-$RID/publish"
CLI_PUBLISH="$PUBLISH/cli"
APPSTREAM_ID="com.abishekprabakaran.IntelliFillOCR"

case "$RID" in
  linux-x64)
    DEB_ARCH="amd64"
    RPM_ARCH="x86_64"
    ARCH_ARCH="x86_64"
    ;;
  linux-arm64)
    DEB_ARCH="arm64"
    RPM_ARCH="aarch64"
    ARCH_ARCH="aarch64"
    ;;
  *)
    echo "Unsupported Linux runtime: $RID. Use linux-x64 or linux-arm64." >&2
    exit 2
    ;;
esac

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
install -d -m 0755 "$PUBLISH/packaging"
install -m 0644 "$ROOT/assets/logo_512.png" "$PUBLISH/packaging/intellifill-ocr.png"
python3 "$ROOT/scripts/generate-linux-metadata.py" \
  "$VERSION" \
  "$ROOT/src/IntelliFillOCR.Avalonia/MainWindow.axaml.cs" \
  "$ROOT/packaging/linux/$APPSTREAM_ID.metainfo.xml.in" \
  "$ROOT/packaging/linux/changelog.in" \
  "$PUBLISH/packaging/$APPSTREAM_ID.metainfo.xml" \
  "$PUBLISH/packaging/changelog"
cat > "$PUBLISH/packaging/intellifill-ocr.desktop" <<'PORTABLE_DESKTOP'
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
PORTABLE_DESKTOP

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
  "$PKG_ROOT/usr/share/metainfo" \
  "$PKG_ROOT/usr/share/doc/intellifill-ocr" \
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
install -m 644 "$PUBLISH/packaging/$APPSTREAM_ID.metainfo.xml" "$PKG_ROOT/usr/share/metainfo/$APPSTREAM_ID.metainfo.xml"
install -m 644 "$PUBLISH/packaging/changelog" "$PKG_ROOT/usr/share/doc/intellifill-ocr/changelog"
gzip -n -9 -c "$PUBLISH/packaging/changelog" > "$PKG_ROOT/usr/share/doc/intellifill-ocr/changelog.Debian.gz"
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
Architecture: $DEB_ARCH
Maintainer: IntelliFill OCR
Depends: libx11-6, libice6, libsm6, libfontconfig1
Homepage: https://abishekprabakaran.com/intellifill-ocr/
Description: Offline OCR document automation and traceable exports
 IntelliFill OCR extracts content locally from PDFs, images, spreadsheets,
 documents, and text files. It fills templates, validates business data, stores
 SQLite audit history, and exports PDF, Word, Excel, and CSV files.
 .
 The package includes both the graphical desktop application and the complete
 intellifill command-line workflow for unattended automation.
CONTROL

DEB="$OUT/intellifill-ocr_${VERSION}_${DEB_ARCH}.deb"
dpkg-deb --build "$PKG_ROOT" "$DEB"

if ! command -v bsdtar >/dev/null 2>&1 || ! command -v zstd >/dev/null 2>&1; then
  echo "bsdtar and zstd are required to create the Arch Linux package." >&2
  exit 1
fi

ARCH_ROOT="$OUT/archroot"
rm -rf "$ARCH_ROOT"
mkdir -p "$ARCH_ROOT"
cp -a "$PKG_ROOT/usr" "$ARCH_ROOT/"
rm -rf "$ARCH_ROOT/usr/share/keyrings" \
  "$ARCH_ROOT/usr/share/intellifill-ocr/repositories/intellifill-ocr.repo" \
  "$ARCH_ROOT/usr/share/intellifill-ocr/packaging"
cat > "$ARCH_ROOT/.PKGINFO" <<PKGINFO
pkgname = intellifill-ocr
pkgbase = intellifill-ocr
pkgver = $VERSION-1
pkgdesc = Offline OCR extraction, table filling, SQLite storage, and traceable exports
url = https://abishekprabakaran.com/intellifill-ocr/
builddate = $(date +%s)
packager = IntelliFill OCR
size = $(du -sb "$ARCH_ROOT/usr" | cut -f1)
arch = $ARCH_ARCH
license = MIT
depend = libx11
depend = libice
depend = libsm
depend = fontconfig
PKGINFO
cat > "$ARCH_ROOT/.INSTALL" <<'ARCH_INSTALL'
post_install() {
  /usr/share/intellifill-ocr/repositories/install-linux-repository.sh || true
  command -v update-desktop-database >/dev/null && update-desktop-database /usr/share/applications || true
  command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
}
post_upgrade() { post_install; }
post_remove() {
  command -v update-desktop-database >/dev/null && update-desktop-database /usr/share/applications || true
  command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
}
ARCH_INSTALL
LANG=C bsdtar -czf "$ARCH_ROOT/.MTREE" \
  --format=mtree \
  --options='!all,use-set,type,uid,gid,mode,time,size,md5,sha256,link' \
  -C "$ARCH_ROOT" .PKGINFO .INSTALL usr
ARCH_PACKAGE="$OUT/intellifill-ocr-$VERSION-1-$ARCH_ARCH.pkg.tar.zst"
bsdtar --uid 0 --gid 0 -C "$ARCH_ROOT" -cf - . | zstd -q -19 -T0 -o "$ARCH_PACKAGE"

if ! command -v alien >/dev/null 2>&1; then
  echo "alien was not found. Install it to convert the Debian package to RPM." >&2
  exit 1
fi

(
  cd "$OUT"
  alien --to-rpm --keep-version "$(basename "$DEB")"
)

echo "Linux packages created in $OUT"
