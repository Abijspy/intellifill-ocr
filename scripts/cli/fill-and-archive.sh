#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 4 ]]; then
  echo "Usage: $0 <template> <source> <output-directory> <sqlite-database> [formats]" >&2
  exit 64
fi

template="$1"
source_document="$2"
output_directory="$3"
database="$4"
formats="${5:-xlsx,pdf}"

intellifill fill \
  --template "$template" \
  --source "$source_document" \
  --output "$output_directory" \
  --format "$formats" \
  --save-db "$database"
