# Flatpak packaging

The Flatpak is self-contained: it includes the IntelliFill OCR GUI and CLI,
Tesseract 5.5.3, Leptonica 1.87.0, and the English and orientation models. It
does not access the host distribution's Tesseract or package repositories.

Install the Freedesktop 25.08 SDK and build a bundle for the current CPU:

```bash
flatpak remote-add --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install --user -y flathub org.freedesktop.Platform//25.08 org.freedesktop.Sdk//25.08
bash scripts/package-flatpak.sh 6.3.2 linux-x64 Release
flatpak install --user release/flatpak/IntelliFillOCR-6.3.2-linux-x64.flatpak
```

Use `linux-arm64` on an ARM64 build machine. Flatpak native dependencies must
be compiled on the matching architecture; this script intentionally rejects
cross-architecture builds.

Run the desktop app or CLI with:

```bash
flatpak run com.abishekprabakaran.IntelliFillOCR
flatpak run --command=intellifill com.abishekprabakaran.IntelliFillOCR --help
```
