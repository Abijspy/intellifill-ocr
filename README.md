<p align="center">
  <img src="assets/logo.png" alt="IntelliFill OCR branding" width="720">
</p>

<p align="center">
  Offline OCR, document extraction, table filling, validation, and traceable exports for Windows, macOS, and Linux.
</p>

<p align="center">
  <a href="https://abishekprabakaran.com/intellifill-ocr/">Website</a> ·
  <a href="https://github.com/Abijspy/intellifill-ocr/releases/latest">Latest release</a> ·
  <a href="LICENSE">GPL-3.0 License</a>
</p>

[![IntelliFill OCR Builder](https://github.com/Abijspy/intellifill-ocr/actions/workflows/release.yml/badge.svg)](https://github.com/Abijspy/intellifill-ocr/actions/workflows/release.yml)

# IntelliFill OCR Desktop

IntelliFill OCR is a local-first desktop application for extracting information from documents, filling structured templates, reviewing results, saving audit data to SQLite, and exporting traceable PDF, Word, Excel, and CSV files. The desktop interface is built with Avalonia and does not require a cloud OCR API.

## Highlights

- Native desktop interface for Windows, macOS, and Linux.
- Imports CSV, TXT, XLSX, DOCX, PDF, PNG, JPG, and JPEG templates.
- Uploads up to five source documents with parsed-text and detected-table previews.
- Image and PDF workspace with zoom, rotation, and rectangular OCR region selection.
- Local Tesseract OCR detection and configuration.
- Manual field mapping and local fuzzy-matching Auto Fill.
- Editable output preview and validation for required values, GST/GSTIN, dates, amounts, and duplicates.
- Local SQLite run history and database preview.
- CSV, XLSX, DOCX, and multi-page PDF exports.
- Configurable traceability IDs shared by database records, filenames, previews, exports, and barcodes.
- System, light, and dark themes with selectable cross-platform accent colors.
- Windows installer updates and native APT, DNF, pacman, and Solus eopkg packaging on Linux.

## Supported Platforms

| Platform | Architecture | Recommended package | Update method |
|---|---:|---|---|
| Windows 10/11 | x64 | NSIS setup `.exe` | In-app release check |
| macOS 11+ | Apple Silicon | Native `.app` in ARM64 `.dmg` | In-app release check |
| macOS 11+ | Intel x64 | Native `.app` in x64 `.dmg` | In-app release check |
| Debian/Ubuntu | amd64 or ARM64 | APT repository or `.deb` | `apt upgrade` |
| Fedora/RHEL | x86_64 or ARM64 | DNF repository or `.rpm` | `dnf upgrade` |
| Arch/Manjaro | x86_64 or ARM64 | pacman repository or `.pkg.tar.zst` | `pacman -Syu` |
| Solus | x86_64 | Native `.eopkg` recipe | `eopkg upgrade` after repository acceptance |
| Other Linux | x64 or ARM64 | Portable `.tar.gz` | Replace with a newer release |

Linux releases support x86_64/amd64 and ARM64/aarch64. ARMv7, x86/i386, and other 32-bit Linux architectures are not supported.

## Windows Installation

### Install with the setup program

1. Open the [latest GitHub release](https://github.com/Abijspy/intellifill-ocr/releases/latest).
2. Download `IntelliFillOCR-<version>-setup-win-x64.exe`.
3. Run the installer. If Windows SmartScreen appears, confirm that the file came from this official repository before continuing.
4. Keep the optional Tesseract OCR component selected if Tesseract is not already installed.
5. Start IntelliFill OCR from the Start Menu or desktop shortcut.

The installer creates a per-user installation under:

```text
%LOCALAPPDATA%\Programs\IntelliFill OCR
```

It also creates Start Menu and optional desktop shortcuts, plus an uninstall entry in Windows Settings and Control Panel.

### Configure Tesseract on Windows

OCR region extraction requires Tesseract. The installer can install Tesseract 5.5.0, or you can install it separately and configure its executable in **Settings → Local Paths**.

The common path is:

```text
C:\Program Files\Tesseract-OCR\tesseract.exe
```

Use **Auto Detect** first. If detection fails, select the executable with **Browse**.

### Update on Windows

Open **Settings → Software Updates → Check for Updates**. The app checks the latest GitHub release and can download and launch the matching Windows installer. Close active document work before completing an installer update.

You can also download and run the newest setup program manually. Installing a newer version over the existing installation preserves user settings and the SQLite database stored in your local application-data folder.

### Uninstall on Windows

Open **Settings → Apps → Installed apps**, select **IntelliFill OCR**, and choose **Uninstall**. The application installation is removed; locally created exports and user-selected SQLite databases are not deleted.

## macOS Installation

1. Open the [latest GitHub release](https://github.com/Abijspy/intellifill-ocr/releases/latest).
2. Download `IntelliFillOCR-<version>-osx-arm64.dmg` for Apple Silicon or `IntelliFillOCR-<version>-osx-x64.dmg` for an Intel Mac.
3. Open the disk image and drag **IntelliFill OCR** into **Applications**.
4. Launch it from Applications. Early unsigned community builds may require **Control-click → Open** and confirmation in **System Settings → Privacy & Security**.
5. Install Tesseract with Homebrew when OCR is needed: `brew install tesseract`.

The DMG contains a self-contained native `.app`; users do not need to install .NET. Both architectures include the desktop application, local PDF/image dependencies, and the complete CLI. To expose the CLI after copying the app:

```bash
sudo ln -sf "/Applications/IntelliFill OCR.app/Contents/Resources/cli/intellifill" /usr/local/bin/intellifill
intellifill --help
```

Use **Settings → Software Updates → Check for Updates** to check GitHub Releases. On macOS the app selects the DMG matching the running Intel or Apple Silicon architecture and opens the release page for installation.

To uninstall, remove **IntelliFill OCR.app** from Applications. User-created exports and databases are not deleted.

## Linux Installation

The recommended Linux installation uses the official package repository. Once configured, IntelliFill OCR updates alongside the rest of the system through APT, DNF, or pacman. The repositories retain the three newest package versions; older artifacts remain available from [GitHub Releases](https://github.com/Abijspy/intellifill-ocr/releases).

Repository endpoints:

- APT: `https://packages.abishekprabakaran.com/apt`
- DNF: `https://packages.abishekprabakaran.com/rpm/$basearch`
- pacman: `https://packages.abishekprabakaran.com/arch/$arch`
- Signing key: `https://packages.abishekprabakaran.com/keys/intellifill-ocr-archive-keyring.gpg`

### Automatic APT/DNF/pacman repository installer

Download and inspect the distro-detecting installer, then run it with administrator access:

```bash
curl -fsSL \
  https://abishekprabakaran.com/intellifill-ocr/install-linux-repository.sh \
  -o /tmp/intellifill-repository.sh
less /tmp/intellifill-repository.sh
sudo bash /tmp/intellifill-repository.sh
```

The script detects Debian/Ubuntu APT, Fedora/RHEL DNF, or Arch/Manjaro pacman, rejects unsupported and 32-bit architectures, installs the correct repository definition and signing key, and refreshes package metadata. Then install the application:

```bash
sudo apt install intellifill-ocr
# or
sudo dnf install intellifill-ocr
# or
sudo pacman -S intellifill-ocr
```

Update later with normal system maintenance:

```bash
sudo apt update && sudo apt upgrade
# or
sudo dnf upgrade
# or
sudo pacman -Syu
```

### Automatic repository setup from the app

Version 5.1.0 and newer provides **Settings → Software Updates → Check Linux Package Repository**. It verifies the repository and launches the same packaged Bash installer through desktop administrator authentication when setup is needed.

The app does not download or replace Linux packages itself. After repository setup, APT, DNF, or pacman remains responsible for checking, installing, rolling back, and auditing system packages.

### Install a downloaded DEB, RPM, or pacman package

If you do not want to configure a repository, download a package from the [latest release](https://github.com/Abijspy/intellifill-ocr/releases/latest).

Debian/Ubuntu:

```bash
sudo apt install ./intellifill-ocr_<version>_amd64.deb
# ARM64: sudo apt install ./intellifill-ocr_<version>_arm64.deb
```

Fedora/RHEL:

```bash
sudo dnf install ./intellifill-ocr-<version>-1.x86_64.rpm
# ARM64: sudo dnf install ./intellifill-ocr-<version>-1.aarch64.rpm
```

Arch/Manjaro:

```bash
sudo pacman -U ./intellifill-ocr-<version>-1-x86_64.pkg.tar.zst
# ARM64: sudo pacman -U ./intellifill-ocr-<version>-1-aarch64.pkg.tar.zst
```

Solus uses its native `solbuild`/`ypkg` toolchain. Generate the version-pinned recipe from the release tarball and build the `.eopkg` as documented in [`packaging/solus/README.md`](packaging/solus/README.md). Publication in the official Solus repository requires review by the Solus packaging team.

The packaged installer adds the official repository automatically. If the repository was unavailable during installation, use the in-app repository check or the repository installer script above.

### Portable Linux archive

The release also includes `IntelliFillOCR-<version>-linux-<x64|arm64>.tar.gz`. Extract it to a user-writable directory and run `IntelliFillOCR`. Portable installations do not register a desktop launcher or package repository automatically.

### Linux desktop launcher and icon

DEB, RPM, and pacman packages install:

```text
/usr/bin/intellifill-ocr
/usr/share/applications/intellifill-ocr.desktop
/usr/share/icons/hicolor/512x512/apps/intellifill-ocr.png
```

If the launcher or icon does not appear immediately, sign out and back in or refresh the desktop database and icon cache:

```bash
sudo update-desktop-database /usr/share/applications 2>/dev/null || true
sudo gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor 2>/dev/null || true
```

### Remove IntelliFill OCR on Linux

Debian/Ubuntu:

```bash
sudo apt remove intellifill-ocr
sudo rm -f /etc/apt/sources.list.d/intellifill-ocr.list
sudo apt update
```

Fedora/RHEL:

```bash
sudo dnf remove intellifill-ocr
sudo rm -f /etc/yum.repos.d/intellifill-ocr.repo
```

Arch/Manjaro:

```bash
sudo pacman -R intellifill-ocr
sudo sed -i '\|Include = /etc/pacman.d/intellifill-ocr.conf|d' /etc/pacman.conf
sudo rm -f /etc/pacman.d/intellifill-ocr.conf
sudo pacman -Sy
```

Removing the package does not delete exports or user-selected database files.

## Repository Security

- APT metadata is published with a signed `InRelease` file and a key restricted through `signed-by`.
- Do not add `trusted=yes` to the APT source.
- DNF validates signed repository metadata with `repo_gpgcheck=1`.
- Package repositories and release assets are rebuilt by GitHub Actions from version tags.
- The custom package domain is backed by the project's GitHub Pages deployment.

## First-Run Configuration

1. Open **Settings → Local Paths** and auto-detect or select Tesseract.
2. Confirm or choose the SQLite database path.
3. Select the system, light, or dark theme and preferred accent color.
4. Choose a traceability-ID mode.
5. Run **Diagnostics and Storage → Run System Readiness Check**.
6. On Linux, verify the package source under **Software Updates**.

Application settings and logs use the operating system's local application-data location under an `IntelliFillOCR` directory. The default SQLite database is also created there unless you select a different path.

## Basic Workflow

1. Upload the template that defines the output tables.
2. Upload one or more source documents.
3. Review detected text and tables, or select an image/PDF region for OCR.
4. Map extracted fields to destination cells manually or use Auto Fill.
5. Correct output values and run validation.
6. Save the run to SQLite when an audit record is needed.
7. Preview and export PDF, Word, Excel, or CSV output.

## Command-Line Interface

The Windows, macOS, and Linux packages include the `intellifill` command. Version 6 uses the same local loaders, field-matching logic, validation, exporters, traceability IDs, and SQLite history as the desktop application. PDF and image OCR runs locally through Tesseract; supported office documents are parsed natively.

### Scan and batch processing

Scan an invoice and create XLSX and PDF output in the current directory:

```bash
intellifill scan invoice.pdf
```

Choose formats and an output directory:

```bash
intellifill scan receipt.png --format csv,xlsx,docx,pdf --output ./exports
```

Select another Tesseract language or executable:

```bash
intellifill scan invoice.pdf --language eng --tesseract /usr/bin/tesseract
```

Scan without exporting, return JSON, or process several documents with the same settings:

```bash
intellifill scan report.docx --no-export
intellifill scan invoice.pdf --no-export --json
intellifill batch scans/*.pdf --format xlsx,pdf --output ./exports
```

Scan and batch options:

| Option | Purpose |
|---|---|
| `-o, --output <directory>` | Select the export directory. |
| `-f, --format <list>` | Select comma-separated `csv`, `xlsx`, `docx`, and `pdf` formats. |
| `-l, --language <code>` | Select the installed Tesseract language; the default is `eng`. |
| `--tesseract <path>` | Use an explicit Tesseract executable. |
| `--no-export` | Extract and validate without writing exports. |
| `--json` | Print structured JSON suitable for automation. |

### Inspect, fill, and validate

Inspect extracted labels and values before filling a template:

```bash
intellifill inspect invoice.pdf --json
```

Fill a template from as many as five source documents, export the result, and preserve the run in SQLite:

```bash
intellifill fill \
  --template template.xlsx \
  --source invoice.pdf \
  --source purchase-order.docx \
  --output ./exports \
  --format xlsx,pdf \
  --save-db ./records/intellifill.db
```

Fill options:

| Option | Purpose |
|---|---|
| `--template <file>` | Required output template. |
| `--source <file>` | Required input; repeat up to five times. |
| `-o, --output <directory>` | Export directory. |
| `-f, --format <list>` | Comma-separated `csv`, `xlsx`, `docx`, and `pdf`. |
| `--threshold <0-1>` | Minimum automatic field-match score; default `0.42`. |
| `--set T,R,C=value` | Override a 1-based table, row, and column; repeat as needed. |
| `--trace-id <id>` | Use an explicit traceability ID. |
| `--trace-mode <mode>` | Use `timestamp`, `prefix-timestamp`, `prefix-random`, or `manual`. |
| `--trace-value <value>` | Prefix or manual value used by the selected trace mode. |
| `--save-db <file>` | Save values and mappings to SQLite. |
| `--no-export` | Fill and validate without creating export files. |
| `--json` | Return a structured automation report. |

Validate a document independently, or inspect saved audit history:

```bash
intellifill validate completed.xlsx
intellifill history ./records/intellifill.db --json
```

Validation warnings return exit code `2`; command or input errors return `1`; success returns `0`.

### Diagnostics and maintenance

```bash
intellifill trace-id --mode prefix-random --value ACME-
intellifill health --tesseract /usr/bin/tesseract --database ./records/intellifill.db
intellifill signatures contract.pdf stamp-scan.png
intellifill update check --json
```

On Linux, check or configure the native package repository. Repository installation opens the normal PolicyKit administrator prompt.

```bash
intellifill repo status
intellifill repo install
```

`signatures` identifies documents that need visual signature/stamp review; it does not claim cryptographic signature verification.

### Ready-made automation scripts

Reusable Bash and PowerShell examples live in [`scripts/cli`](scripts/cli):

```bash
bash scripts/cli/batch-scan.sh ./incoming ./exports xlsx,pdf
bash scripts/cli/validate-folder.sh ./completed
bash scripts/cli/fill-and-archive.sh template.xlsx invoice.pdf ./exports ./records/runs.db
```

```powershell
.\scripts\cli\batch-scan.ps1 -InputDirectory .\incoming -OutputDirectory .\exports
.\scripts\cli\fill-and-archive.ps1 -Template template.xlsx -Source invoice.pdf -OutputDirectory .\exports -Database .\records\runs.db
```

Run `intellifill --help` for the command summary or `intellifill --version` for the installed version. On Windows, open a new terminal after installation so the updated user `PATH` is loaded. The same complete guide is also published on the [project website](https://abishekprabakaran.com/intellifill-ocr/#cli-reference).

## Troubleshooting

### APT reports that the repository has no Release file

Confirm the source uses the exact HTTPS URL and distribution shown below:

```text
deb [arch=amd64 signed-by=/usr/share/keyrings/intellifill-ocr-archive-keyring.gpg] https://packages.abishekprabakaran.com/apt stable main
```

Then run `sudo apt update`. If the error continues, check the [Actions page](https://github.com/Abijspy/intellifill-ocr/actions) because repository publication may still be running after a new release.

### APT mentions an unsupported i386 Packages file

Ensure the source includes `arch=amd64`. Remove older IntelliFill OCR source entries that omit the architecture restriction.

### APT, DNF, or pacman does not find a newly released update

The release builder completes before the package-repository publisher. Wait for both GitHub Actions workflows to finish, then refresh metadata:

```bash
sudo apt update
# or
sudo dnf clean metadata && sudo dnf makecache
# or
sudo pacman -Syy
```

### OCR is unavailable

Install Tesseract, then use **Settings → Local Paths → Auto Detect**. Confirm the configured executable exists and run the system readiness check.

### Linux repository setup cannot request administrator access

The in-app setup uses `pkexec`. Install the distribution's PolicyKit integration or run the repository installer script from this README with `sudo`.

## Build Locally

Requirements:

- .NET 8 SDK
- Tesseract OCR for region-extraction testing
- NSIS 3.x for Windows installer builds
- macOS with `hdiutil`, `iconutil`, `sips`, `plutil`, and `codesign` for DMG builds
- `dpkg-deb`, RPM tools, `alien`, `bsdtar`, and `zstd` for Linux package builds

Build the application:

```bash
dotnet build src/IntelliFillOCR.Avalonia/IntelliFillOCR.Avalonia.csproj -c Release
```

Build the Windows installer from PowerShell:

```powershell
.\scripts\package-release.ps1 -Version 6.0.0 -RuntimeIdentifier win-x64
```

Output:

```text
installer\out\IntelliFillOCR-6.0.0-setup-win-x64.exe
```

Build Linux packages on Linux:

```bash
bash scripts/package-linux.sh 6.0.0 linux-x64 Release
# or: bash scripts/package-linux.sh 6.0.0 linux-arm64 Release
```

Linux outputs are written under `release/linux/`.

Build a macOS disk image on a Mac:

```bash
bash scripts/package-macos.sh 6.0.0 osx-arm64 Release
# or: bash scripts/package-macos.sh 6.0.0 osx-x64 Release
```

macOS outputs are written under `release/macos/`.

## Release Automation

The `IntelliFill OCR Builder` workflow in `.github/workflows/release.yml` produces:

- Windows NSIS setup EXE
- Apple Silicon and Intel macOS DMG images
- Debian/Ubuntu DEB
- Fedora/RHEL RPM
- Arch/Manjaro pacman package
- Solus native eopkg recipe
- Portable Linux tarball

The package-repository workflow then publishes signed APT metadata, DNF metadata, and a signed Arch pacman repository to the `gh-pages` deployment, retaining the three newest Linux package versions.

Create a release by pushing a version tag:

```bash
git tag v6.0.0
git push origin v6.0.0
```

The workflows can also be started manually from GitHub Actions with an explicit version.

## Repository Layout

```text
src/IntelliFillOCR.Avalonia/      Avalonia desktop application
src/IntelliFillOCR.Cli/           Cross-platform intellifill command
src/IntelliFillOCR.Core/          Document, export, and SQLite services
installer/IntelliFillOCR.nsi      Windows NSIS installer
scripts/                          Build, version, and packaging scripts
scripts/package-macos.sh          macOS app-bundle and DMG builder
package-repository/               Repository signing material and configuration
assets/                           Application icons and branding
demo/                             Small CSV demonstration fixtures
website/                          Static official project website
```

## License

IntelliFill OCR is free software licensed under the [GNU General Public License v3.0](LICENSE).
