# IntelliFill OCR Desktop

## Linux package repositories

The signed package repositories are automatically rebuilt from each GitHub release. They are served from the repository's `gh-pages` branch using GitHub's HTTPS raw-content endpoint:

- APT (Debian/Ubuntu): `https://packages.abishekprabakaran.com/apt`
- DNF (Fedora/RHEL): `https://packages.abishekprabakaran.com/rpm/$basearch`

### Debian and Ubuntu

```bash
curl -fsSL https://packages.abishekprabakaran.com/keys/intellifill-ocr-archive-keyring.gpg | \
  sudo gpg --dearmor -o /usr/share/keyrings/intellifill-ocr-archive-keyring.gpg
echo "deb [arch=amd64 signed-by=/usr/share/keyrings/intellifill-ocr-archive-keyring.gpg] https://packages.abishekprabakaran.com/apt stable main" | \
  sudo tee /etc/apt/sources.list.d/intellifill-ocr.list
sudo apt update
sudo apt install intellifill-ocr
```

### Fedora and RHEL

```bash
sudo curl -fsSL https://packages.abishekprabakaran.com/intellifill-ocr.repo \
  -o /etc/yum.repos.d/intellifill-ocr.repo
sudo dnf install intellifill-ocr
```

After setup, update with `sudo apt upgrade` or `sudo dnf upgrade`.

### Updating

Once installed, normal system updates include IntelliFill OCR:

```bash
sudo apt update && sudo apt upgrade
# or
sudo dnf upgrade
```

### Architecture and security

- The current repositories publish `amd64` / `x86_64` packages only.
- APT uses a signed `InRelease` file and a pinned keyring; do not add `trusted=yes`.
- DNF validates signed repository metadata (`repo_gpgcheck=1`).

### Removing the repository

```bash
sudo rm -f /etc/apt/sources.list.d/intellifill-ocr.list
sudo rm -f /etc/yum.repos.d/intellifill-ocr.repo
sudo apt update  # Debian/Ubuntu only
```

### Custom domain

A dedicated package subdomain can replace the raw GitHub URL once DNS is configured. Until then, the URLs above are the supported update endpoints.
