#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <version> <IntelliFillOCR-version-linux-x64.tar.gz>" >&2
  exit 2
fi

VERSION="$1"
ARCHIVE="$2"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.][0-9]+)?$ ]] || { echo "Invalid version: $VERSION" >&2; exit 2; }
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TEMPLATE="$ROOT/packaging/solus/package.yml.in"
OUTPUT="$ROOT/packaging/solus/package.yml"

[[ -f "$ARCHIVE" ]] || { echo "Archive not found: $ARCHIVE" >&2; exit 1; }
SHA256="$(sha256sum "$ARCHIVE" | cut -d' ' -f1)"
sed -e "s/@VERSION@/$VERSION/g" -e "s/@SHA256@/$SHA256/g" "$TEMPLATE" > "$OUTPUT"
echo "Created $OUTPUT"
echo "Build on Solus with: sudo solbuild build $OUTPUT"
