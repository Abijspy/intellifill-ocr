#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_HOST="https://packages.abishekprabakaran.com"
APT_SOURCE="/etc/apt/sources.list.d/intellifill-ocr.list"
APT_KEYRING="/usr/share/keyrings/intellifill-ocr-archive-keyring.gpg"
DNF_SOURCE="/etc/yum.repos.d/intellifill-ocr.repo"

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  echo "Usage: sudo bash install-linux-repository.sh"
  echo "Detects an APT or DNF Linux distribution and installs the IntelliFill OCR package repository."
  exit 0
fi

if [[ "${EUID}" -ne 0 ]]; then
  if command -v pkexec >/dev/null 2>&1; then
    exec pkexec bash "$0" "$@"
  fi
  if command -v sudo >/dev/null 2>&1; then
    exec sudo bash "$0" "$@"
  fi
  echo "Run this installer as root or install sudo/pkexec." >&2
  exit 1
fi

architecture="$(uname -m)"
if [[ "$architecture" != "x86_64" && "$architecture" != "amd64" ]]; then
  echo "Unsupported architecture: $architecture. IntelliFill OCR repositories currently publish x86_64/amd64 packages only." >&2
  exit 2
fi

if ! command -v curl >/dev/null 2>&1; then
  echo "curl is required to install the repository." >&2
  exit 3
fi

distribution="Linux"
distribution_id=""
distribution_like=""
if [[ -r /etc/os-release ]]; then
  # Values in os-release are controlled by the operating system.
  . /etc/os-release
  distribution="${PRETTY_NAME:-Linux}"
  distribution_id="${ID:-}"
  distribution_like="${ID_LIKE:-}"
fi
family=" $distribution_id $distribution_like "

if [[ "$family" == *debian* || "$family" == *ubuntu* ]] || command -v apt-get >/dev/null 2>&1; then
  echo "Detected $distribution (APT)."
  install -d -m 0755 /usr/share/keyrings /etc/apt/sources.list.d
  curl -fsSL "$REPOSITORY_HOST/keys/intellifill-ocr-archive-keyring.gpg" -o "$APT_KEYRING"
  chmod 0644 "$APT_KEYRING"
  printf '%s\n' "deb [arch=amd64 signed-by=$APT_KEYRING] $REPOSITORY_HOST/apt stable main" > "$APT_SOURCE"
  chmod 0644 "$APT_SOURCE"
  apt-get update
  echo "Repository installed. Use: sudo apt install intellifill-ocr"
  exit 0
fi

if [[ "$family" == *fedora* || "$family" == *rhel* || "$family" == *centos* ]] || command -v dnf >/dev/null 2>&1; then
  echo "Detected $distribution (DNF)."
  install -d -m 0755 /etc/yum.repos.d
  cat > "$DNF_SOURCE" <<REPOSITORY
[intellifill-ocr]
name=IntelliFill OCR
baseurl=$REPOSITORY_HOST/rpm/\$basearch
enabled=1
gpgcheck=0
repo_gpgcheck=1
gpgkey=$REPOSITORY_HOST/keys/intellifill-ocr-archive-keyring.gpg
REPOSITORY
  chmod 0644 "$DNF_SOURCE"
  dnf makecache
  echo "Repository installed. Use: sudo dnf install intellifill-ocr"
  exit 0
fi

echo "Unsupported distribution: $distribution. Automatic setup supports Debian/Ubuntu APT and Fedora/RHEL DNF systems." >&2
exit 4
