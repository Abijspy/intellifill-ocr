#!/usr/bin/env fish

set -l repository_host "https://packages.abishekprabakaran.com"

function show_help
    echo "Usage: sudo fish install-linux-repository.fish"
    echo "Detects APT, DNF, or pacman distributions and installs the IntelliFill OCR repository."
end

if contains -- --help $argv; or contains -- -h $argv
    show_help
    exit 0
end

if test (id -u) -ne 0
    if type -q pkexec
        exec pkexec fish (status filename) $argv
    end
    if type -q sudo
        exec sudo fish (status filename) $argv
    end
    echo "Run this installer as root or install sudo/pkexec." >&2
    exit 1
end

set -l architecture (uname -m)
switch $architecture
    case x86_64 amd64
        set deb_arch amd64
    case aarch64 arm64
        set deb_arch arm64
    case '*'
        echo "Unsupported architecture: $architecture. IntelliFill OCR supports x86_64 and ARM64 Linux." >&2
        exit 2
end

if not type -q curl
    echo "curl is required to install the repository." >&2
    exit 3
end

set -l distribution Linux
set -l family ""
if test -r /etc/os-release
    set -l pretty (string match -r '^PRETTY_NAME=' < /etc/os-release | head -n 1 | string replace -r '^PRETTY_NAME=["\']?([^"\']*)["\']?$' '$1')
    set -l distro_id (string match -r '^ID=' < /etc/os-release | head -n 1 | string replace -r '^ID=["\']?([^"\']*)["\']?$' '$1')
    set -l distro_like (string match -r '^ID_LIKE=' < /etc/os-release | head -n 1 | string replace -r '^ID_LIKE=["\']?([^"\']*)["\']?$' '$1')
    test -n "$pretty"; and set distribution $pretty
    set family "$distro_id $distro_like"
end
set family (string lower -- $family)

if string match -q '*arch*' -- $family; or string match -rq 'manjaro|endeavouros|cachyos|garuda|arcolinux|artix|rebornos|crystal' -- $family; or type -q pacman
    echo "Detected $distribution (pacman)."
    install -d -m 0755 /etc/pacman.d
    set -l key_file (mktemp)
    curl -fsSL "$repository_host/keys/intellifill-ocr-archive-keyring.gpg" -o "$key_file"; or exit 5
    set -l fingerprint (gpg --show-keys --with-colons "$key_file" | awk -F: '$1 == "fpr" { print $10; exit }')
    test -n "$fingerprint"; or begin; echo "Could not read the repository signing-key fingerprint." >&2; exit 5; end
    pacman-key --add "$key_file"; and pacman-key --lsign-key "$fingerprint"; or exit 5
    rm -f "$key_file"
    printf '%s\n' '[intellifill-ocr]' 'SigLevel = Required DatabaseOptional' "Server = $repository_host/arch/\$arch" > /etc/pacman.d/intellifill-ocr.conf
    if not grep -qF 'Include = /etc/pacman.d/intellifill-ocr.conf' /etc/pacman.conf
        printf '\n%s\n' 'Include = /etc/pacman.d/intellifill-ocr.conf' >> /etc/pacman.conf
    end
    pacman -Sy
    echo "Repository installed. Use: sudo pacman -S intellifill-ocr"
    exit 0
end

if string match -rq 'debian|ubuntu' -- $family; or type -q apt-get
    echo "Detected $distribution (APT)."
    install -d -m 0755 /usr/share/keyrings /etc/apt/sources.list.d
    curl -fsSL "$repository_host/keys/intellifill-ocr-archive-keyring.gpg" -o /usr/share/keyrings/intellifill-ocr-archive-keyring.gpg; or exit 5
    chmod 0644 /usr/share/keyrings/intellifill-ocr-archive-keyring.gpg
    printf '%s\n' "deb [arch=$deb_arch signed-by=/usr/share/keyrings/intellifill-ocr-archive-keyring.gpg] $repository_host/apt stable main" > /etc/apt/sources.list.d/intellifill-ocr.list
    apt-get update
    echo "Repository installed. Use: sudo apt install intellifill-ocr"
    exit 0
end

if string match -rq 'fedora|rhel|centos' -- $family; or type -q dnf
    echo "Detected $distribution (DNF)."
    install -d -m 0755 /etc/yum.repos.d
    printf '%s\n' '[intellifill-ocr]' 'name=IntelliFill OCR' "baseurl=$repository_host/rpm/\$basearch" 'enabled=1' 'gpgcheck=0' 'repo_gpgcheck=1' "gpgkey=$repository_host/keys/intellifill-ocr-archive-keyring.gpg" > /etc/yum.repos.d/intellifill-ocr.repo
    dnf makecache
    echo "Repository installed. Use: sudo dnf install intellifill-ocr"
    exit 0
end

echo "Unsupported distribution: $distribution. Automatic setup supports Debian/Ubuntu APT, Fedora/RHEL DNF, and Arch-based pacman systems (including EndeavourOS, CachyOS, Garuda, ArcoLinux, Artix, RebornOS, and Crystal Linux)." >&2
exit 4
