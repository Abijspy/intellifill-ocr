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

DEB_ROOT="$OUT/debroot"
rm -rf "$DEB_ROOT"
mkdir -p "$DEB_ROOT/DEBIAN" "$DEB_ROOT/usr/share/intellifill-ocr" "$DEB_ROOT/usr/bin" "$DEB_ROOT/usr/share/applications"
cp -a "$PUBLISH/." "$DEB_ROOT/usr/share/intellifill-ocr/"
cat > "$DEB_ROOT/DEBIAN/control" <<CONTROL
Package: intellifill-ocr
Version: $VERSION
Section: utils
Priority: optional
Architecture: amd64
Maintainer: IntelliFill OCR
Depends: libx11-6, libice6, libsm6, libfontconfig1
Description: Offline OCR extraction, table filling, SQLite storage, and traceable exports.
CONTROL
cat > "$DEB_ROOT/usr/bin/intellifill-ocr" <<'WRAPPER'
#!/usr/bin/env bash
exec /usr/share/intellifill-ocr/IntelliFillOCR "$@"
WRAPPER
chmod 755 "$DEB_ROOT/usr/bin/intellifill-ocr"
cat > "$DEB_ROOT/usr/share/applications/intellifill-ocr.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=IntelliFill OCR
Comment=Offline OCR extraction and table filling
Exec=/usr/bin/intellifill-ocr
Terminal=false
Categories=Office;Utility;
DESKTOP
dpkg-deb --build "$DEB_ROOT" "$OUT/intellifill-ocr_${VERSION}_amd64.deb"

if command -v rpmbuild >/dev/null 2>&1; then
  RPM_ROOT="$OUT/rpmroot"
  RPM_SRC="$OUT/rpmsource"
  rm -rf "$RPM_ROOT" "$RPM_SRC"
  mkdir -p "$RPM_ROOT/BUILD" "$RPM_ROOT/RPMS" "$RPM_ROOT/SOURCES" "$RPM_ROOT/SPECS" "$RPM_ROOT/SRPMS"
  mkdir -p "$RPM_SRC/intellifill-ocr-$VERSION"
  cp -a "$PUBLISH/." "$RPM_SRC/intellifill-ocr-$VERSION/"
  tar -C "$RPM_SRC" -czf "$RPM_ROOT/SOURCES/intellifill-ocr-$VERSION.tar.gz" "intellifill-ocr-$VERSION"
  cat > "$RPM_ROOT/SPECS/intellifill-ocr.spec" <<SPEC
Name: intellifill-ocr
Version: $VERSION
Release: 1%{?dist}
Summary: Offline OCR extraction and table filling
License: Proprietary
Requires: libX11, libICE, libSM, fontconfig

%description
IntelliFill OCR is an offline desktop application for OCR extraction, table filling, SQLite storage, and traceable exports.

%prep
%setup -q

%build

%install
mkdir -p %{buildroot}/usr/share/intellifill-ocr
mkdir -p %{buildroot}/usr/bin
mkdir -p %{buildroot}/usr/share/applications
cp -a * %{buildroot}/usr/share/intellifill-ocr/
cat > %{buildroot}/usr/bin/intellifill-ocr <<'WRAPPER'
#!/usr/bin/env bash
exec /usr/share/intellifill-ocr/IntelliFillOCR "\$@"
WRAPPER
chmod 755 %{buildroot}/usr/bin/intellifill-ocr
cat > %{buildroot}/usr/share/applications/intellifill-ocr.desktop <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=IntelliFill OCR
Comment=Offline OCR extraction and table filling
Exec=/usr/bin/intellifill-ocr
Terminal=false
Categories=Office;Utility;
DESKTOP

%files
/usr/share/intellifill-ocr
/usr/bin/intellifill-ocr
/usr/share/applications/intellifill-ocr.desktop
SPEC
  rpmbuild --define "_topdir $RPM_ROOT" -bb "$RPM_ROOT/SPECS/intellifill-ocr.spec"
  cp "$RPM_ROOT"/RPMS/*/*.rpm "$OUT/"
fi

echo "Linux packages created in $OUT"
