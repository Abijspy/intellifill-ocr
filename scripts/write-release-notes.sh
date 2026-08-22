#!/usr/bin/env bash
set -euo pipefail

version=${1:?version is required}
source_file=${2:?source file is required}

printf "# IntelliFill OCR %s\n\n" "$version"
sed -n '/^        IntelliFill OCR Changelog$/,/^        """;$/p' "$source_file" | sed 's/^        //'
