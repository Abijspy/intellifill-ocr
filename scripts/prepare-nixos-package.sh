#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "Usage: $0 <version> <x64-tarball> <arm64-tarball> <output.nix>" >&2
  exit 2
fi

VERSION="$1"
X64_ARCHIVE="$2"
ARM64_ARCHIVE="$3"
OUTPUT="$4"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.][0-9]+)?$ ]] || { echo "Invalid version: $VERSION" >&2; exit 2; }
[[ -f "$X64_ARCHIVE" ]] || { echo "x64 archive not found: $X64_ARCHIVE" >&2; exit 1; }
[[ -f "$ARM64_ARCHIVE" ]] || { echo "ARM64 archive not found: $ARM64_ARCHIVE" >&2; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TEMPLATE="$ROOT/packaging/nixos/intellifill-ocr.nix.in"
X64_HASH="$(openssl dgst -sha256 -binary "$X64_ARCHIVE" | openssl base64 -A)"
ARM64_HASH="$(openssl dgst -sha256 -binary "$ARM64_ARCHIVE" | openssl base64 -A)"

sed \
  -e "s|@VERSION@|$VERSION|g" \
  -e "s|@X64_HASH@|$X64_HASH|g" \
  -e "s|@ARM64_HASH@|$ARM64_HASH|g" \
  "$TEMPLATE" > "$OUTPUT"

echo "Created $OUTPUT"
