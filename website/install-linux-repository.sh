#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_HOST="https://packages.abishekprabakaran.com"
APT_SOURCE="/etc/apt/sources.list.d/intellifill-ocr.list"
APT_KEYRING="/usr/share/keyrings/intellifill-ocr-archive-keyring.gpg"
DNF_SOURCE="/etc/yum.repos.d/intellifill-ocr.repo"
PACMAN_SOURCE="/etc/pacman.d/intellifill-ocr.conf"

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  echo "Usage: sudo bash install-linux-repository.sh"
  echo "Detects an APT, DNF, or pacman Linux distribution and installs the IntelliFill OCR package repository."
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
case "$architecture" in
  x86_64|amd64) deb_arch="amd64" ;;
  aarch64|arm64) deb_arch="arm64" ;;
  *) echo "Unsupported architecture: $architecture. IntelliFill OCR supports x86_64 and ARM64 Linux." >&2; exit 2 ;;
esac

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

if [[ "$family" == *arch* || "$family" == *manjaro* ]] || command -v pacman >/dev/null 2>&1; then
  echo "Detected $distribution (pacman)."
  install -d -m 0755 /etc/pacman.d
  key_file="$(mktemp)"
  trap 'rm -f "$key_file"' EXIT
  curl -fsSL "$REPOSITORY_HOST/keys/intellifill-ocr-archive-keyring.gpg" -o "$key_file"
  fingerprint="$(gpg --show-keys --with-colons "$key_file" | awk -F: '$1 == "fpr" { print $10; exit }')"
  [[ -n "$fingerprint" ]] || { echo "Could not read the repository signing-key fingerprint." >&2; exit 5; }
  pacman-key --add "$key_file"
  pacman-key --lsign-key "$fingerprint"
  rm -f "$key_file"
  trap - EXIT
  cat > "$PACMAN_SOURCE" <<REPOSITORY
[intellifill-ocr]
SigLevel = Required DatabaseOptional
Server = $REPOSITORY_HOST/arch/\$arch
REPOSITORY
  if ! grep -qF "Include = $PACMAN_SOURCE" /etc/pacman.conf; then
    printf '\n%s\n' "Include = $PACMAN_SOURCE" >> /etc/pacman.conf
  fi
  pacman -Sy
  echo "Repository installed. Use: sudo pacman -S intellifill-ocr"
  exit 0
fi

if [[ "$family" == *debian* || "$family" == *ubuntu* ]] || command -v apt-get >/dev/null 2>&1; then
  echo "Detected $distribution (APT)."
  install -d -m 0755 /usr/share/keyrings /etc/apt/sources.list.d
  curl -fsSL "$REPOSITORY_HOST/keys/intellifill-ocr-archive-keyring.gpg" -o "$APT_KEYRING"
  chmod 0644 "$APT_KEYRING"
  printf '%s\n' "deb [arch=$deb_arch signed-by=$APT_KEYRING] $REPOSITORY_HOST/apt stable main" > "$APT_SOURCE"
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

if [[ "$family" == *solus* ]] || command -v eopkg >/dev/null 2>&1; then
  echo "Detected $distribution (eopkg). IntelliFill OCR includes a native Solus package recipe, but a public eopkg repository is not published yet." >&2
  echo "Build packaging/solus/package.yml with solbuild, then install the resulting package with: sudo eopkg it ./intellifill-ocr-*.eopkg" >&2
  exit 6
fi

echo "Unsupported distribution: $distribution. Automatic repository setup supports Debian/Ubuntu APT, Fedora/RHEL DNF, and Arch-based pacman systems." >&2
exit 4
