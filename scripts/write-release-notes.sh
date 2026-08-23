#!/usr/bin/env bash
set -euo pipefail

version=${1:?version is required}
source_file=${2:?source file is required}

printf "# IntelliFill OCR %s\n\n" "$version"

notes=$(sed 's/^        //' "$source_file" | awk -v version="$version" '
  $0 == "Version " version {
    found = 1
    printing = 1
  }
  printing && $0 ~ /^Version / && $0 != "Version " version {
    exit
  }
  printing { print }
  END { if (!found) exit 2 }
') || {
  printf 'No changelog section found for Version %s in %s\n' "$version" "$source_file" >&2
  exit 1
}

printf '%s\n' "$notes"
