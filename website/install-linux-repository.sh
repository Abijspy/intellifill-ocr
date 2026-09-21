#!/usr/bin/env bash
set -euo pipefail

REPOSITORY_HOST="https://packages.abishekprabakaran.com"
APT_SOURCE="/etc/apt/sources.list.d/intellifill-ocr.list"
APT_KEYRING="/usr/share/keyrings/intellifill-ocr-archive-keyring.gpg"
DNF_SOURCE="/etc/yum.repos.d/intellifill-ocr.repo"
PACMAN_SOURCE="/etc/pacman.d/intellifill-ocr.conf"

ASSUME_YES=false
if [[ "${1:-}" == "--yes" || "${1:-}" == "-y" ]]; then
  ASSUME_YES=true
  shift
fi

if [[ -t 1 ]]; then
  UI_BOLD="$(tput bold 2>/dev/null || true)"
  UI_RESET="$(tput sgr0 2>/dev/null || true)"
  UI_ACCENT="$(tput setaf 6 2>/dev/null || true)"
  UI_OK="$(tput setaf 2 2>/dev/null || true)"
  UI_WARN="$(tput setaf 3 2>/dev/null || true)"
else
  UI_BOLD="" UI_RESET="" UI_ACCENT="" UI_OK="" UI_WARN=""
fi

ui_title() { printf '\n%s%sIntelliFill OCR · Linux repository setup%s\n' "$UI_BOLD" "$UI_ACCENT" "$UI_RESET"; }
ui_step() { printf '%s›%s %s\n' "$UI_ACCENT" "$UI_RESET" "$1"; }
ui_done() { printf '%s✓%s %s\n' "$UI_OK" "$UI_RESET" "$1"; }
ui_warning() { printf '%s!%s %s\n' "$UI_WARN" "$UI_RESET" "$1" >&2; }
confirm_install() {
  local manager="$1"
  [[ "$ASSUME_YES" == true || ! -t 0 ]] && return 0
  printf '\nConfigure the IntelliFill OCR repository for %s? [Y/n] ' "$manager"
  local answer
  read -r answer
  [[ -z "$answer" || "$answer" =~ ^[Yy]([Ee][Ss])?$ ]] || { echo "No changes were made."; exit 0; }
}

download_repository_file() {
  local url="$1" destination="$2"
  if curl --fail --show-error --location --retry 3 --retry-delay 2 --retry-all-errors \
    --connect-timeout 15 --max-time 120 "$url" -o "$destination"; then
    return 0
  fi
  ui_warning "Could not reach $REPOSITORY_HOST after 3 attempts."
  ui_warning "Check your network, DNS, proxy, or firewall, then run this installer again."
  exit 7
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  echo "Usage: sudo bash install-linux-repository.sh [--yes]"
  echo "Detects an APT, DNF, or pacman Linux distribution and installs the IntelliFill OCR package repository."
  echo "Use --yes to skip the confirmation prompt in an interactive terminal."
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
ui_title
ui_step "Checking system architecture…"
case "$architecture" in
  x86_64|amd64) deb_arch="amd64" ;;
  aarch64|arm64) deb_arch="arm64" ;;
  *) echo "Unsupported architecture: $architecture. IntelliFill OCR supports x86_64 and ARM64 Linux." >&2; exit 2 ;;
esac

if ! command -v curl >/dev/null 2>&1; then
  echo "curl is required to install the repository." >&2
  exit 3
fi

ui_done "Architecture supported: $architecture"
ui_step "Detecting your Linux distribution…"

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
ui_done "Detected: $distribution"

if [[ "$family" == *arch* || "$family" == *manjaro* || "$family" == *endeavouros* || "$family" == *cachyos* || "$family" == *garuda* || "$family" == *arcolinux* || "$family" == *artix* || "$family" == *rebornos* || "$family" == *crystal* ]] || command -v pacman >/dev/null 2>&1; then
  echo "Detected $distribution (pacman)."
  confirm_install "pacman"
  ui_step "Downloading and enrolling the repository signing key…"
  install -d -m 0755 /etc/pacman.d
  key_file="$(mktemp)"
  trap 'rm -f "$key_file"' EXIT
  download_repository_file "$REPOSITORY_HOST/keys/intellifill-ocr-archive-keyring.gpg" "$key_file"
  fingerprint="$(gpg --show-keys --with-colons "$key_file" | awk -F: '$1 == "fpr" { print $10; exit }')"
  [[ -n "$fingerprint" ]] || { echo "Could not read the repository signing-key fingerprint." >&2; exit 5; }
  pacman-key --add "$key_file"
  pacman-key --lsign-key "$fingerprint"
  ui_done "Signing key enrolled"
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
  ui_step "Adding the pacman repository and refreshing package metadata…"
  pacman -Sy
  ui_done "Repository is ready"
  echo "Repository installed. Use: sudo pacman -S intellifill-ocr"
  exit 0
fi

if [[ "$family" == *debian* || "$family" == *ubuntu* ]] || command -v apt-get >/dev/null 2>&1; then
  echo "Detected $distribution (APT)."
  confirm_install "APT"
  ui_step "Downloading the repository signing key…"
  install -d -m 0755 /usr/share/keyrings /etc/apt/sources.list.d
  download_repository_file "$REPOSITORY_HOST/keys/intellifill-ocr-archive-keyring.gpg" "$APT_KEYRING"
  chmod 0644 "$APT_KEYRING"
  printf '%s\n' "deb [arch=$deb_arch signed-by=$APT_KEYRING] $REPOSITORY_HOST/apt stable main" > "$APT_SOURCE"
  chmod 0644 "$APT_SOURCE"
  ui_step "Adding the APT source and refreshing package metadata…"
  apt-get update
  ui_done "Repository is ready"
  echo "Repository installed. Use: sudo apt install intellifill-ocr"
  exit 0
fi

if [[ "$family" == *fedora* || "$family" == *rhel* || "$family" == *centos* ]] || command -v dnf >/dev/null 2>&1; then
  echo "Detected $distribution (DNF)."
  confirm_install "DNF"
  ui_step "Writing the DNF repository definition…"
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
  ui_step "Refreshing DNF package metadata…"
  dnf makecache
  ui_done "Repository is ready"
  echo "Repository installed. Use: sudo dnf install intellifill-ocr"
  exit 0
fi

if [[ "$family" == *solus* ]] || command -v eopkg >/dev/null 2>&1; then
  ui_warning "A public eopkg repository is not available yet."
  echo "Detected $distribution (eopkg). IntelliFill OCR includes a native Solus package recipe, but a public eopkg repository is not published yet." >&2
  echo "Build packaging/solus/package.yml with solbuild, then install the resulting package with: sudo eopkg it ./intellifill-ocr-*.eopkg" >&2
  exit 6
fi

echo "Unsupported distribution: $distribution. Automatic repository setup supports Debian/Ubuntu APT, Fedora/RHEL DNF, and Arch-based pacman systems (including Manjaro, EndeavourOS, CachyOS, Garuda, ArcoLinux, Artix, RebornOS, and Crystal Linux)." >&2
exit 4
