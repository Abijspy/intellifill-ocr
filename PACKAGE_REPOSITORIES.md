# IntelliFill OCR Desktop

## Linux package repositories

The signed package repositories are hosted on GitHub Pages after the repository publisher runs:

- APT (Debian/Ubuntu): `https://abijspy.github.io/intellifill-ocr/apt`
- DNF (Fedora/RHEL): `https://abijspy.github.io/intellifill-ocr/rpm/$basearch`

### Debian and Ubuntu

```bash
curl -fsSL https://abijspy.github.io/intellifill-ocr/keys/intellifill-ocr-archive-keyring.gpg | \
  sudo gpg --dearmor -o /usr/share/keyrings/intellifill-ocr-archive-keyring.gpg
echo "deb [signed-by=/usr/share/keyrings/intellifill-ocr-archive-keyring.gpg] https://abijspy.github.io/intellifill-ocr/apt stable main" | \
  sudo tee /etc/apt/sources.list.d/intellifill-ocr.list
sudo apt update
sudo apt install intellifill-ocr
```

### Fedora and RHEL

```bash
sudo curl -fsSL https://abijspy.github.io/intellifill-ocr/intellifill-ocr.repo \
  -o /etc/yum.repos.d/intellifill-ocr.repo
sudo dnf install intellifill-ocr
```

After setup, update with `sudo apt upgrade` or `sudo dnf upgrade`.
