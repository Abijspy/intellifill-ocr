#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <input-directory> <output-directory> [formats]" >&2
  exit 64
fi

input_directory="$1"
output_directory="$2"
formats="${3:-xlsx,pdf}"

[[ -d "$input_directory" ]] || { echo "Input directory not found: $input_directory" >&2; exit 66; }
mkdir -p "$output_directory"

found=0
while IFS= read -r -d '' document; do
  found=1
  intellifill scan "$document" --output "$output_directory" --format "$formats"
done < <(find "$input_directory" -maxdepth 1 -type f \( -iname '*.pdf' -o -iname '*.png' -o -iname '*.jpg' -o -iname '*.jpeg' -o -iname '*.tif' -o -iname '*.tiff' -o -iname '*.csv' -o -iname '*.xlsx' -o -iname '*.docx' \) -print0)

[[ $found -eq 1 ]] || { echo "No supported documents found in: $input_directory" >&2; exit 65; }
