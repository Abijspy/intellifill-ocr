from __future__ import annotations

import json
import re
import shlex
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from intellifill_ocr import __version__
from intellifill_ocr.utils.exceptions import IntelliFillError


ProgressCallback = Callable[[int, int], None]


@dataclass(frozen=True)
class ReleaseAsset:
    name: str
    browser_download_url: str
    size: int = 0
    package_type: str = "unknown"


@dataclass(frozen=True)
class UpdateInfo:
    current_version: str
    latest_version: str
    release_url: str
    release_notes: str
    is_newer: bool
    installer_asset: ReleaseAsset | None
    platform_label: str = ""


class UpdateService:
    """Checks GitHub releases only when the user explicitly asks."""

    LATEST_RELEASE_API = "https://api.github.com/repos/Abijspy/intellifill-ocr/releases/latest"
    USER_AGENT = "IntelliFillOCR-Updater"

    def fetch_latest(self) -> UpdateInfo:
        try:
            request = Request(self.LATEST_RELEASE_API, headers={"User-Agent": self.USER_AGENT})
            with urlopen(request, timeout=20) as response:
                payload = json.loads(response.read().decode("utf-8"))
        except (HTTPError, URLError, TimeoutError, OSError) as exc:
            raise IntelliFillError(f"Could not check for updates: {exc}") from exc

        latest_version = str(payload.get("tag_name") or "").lstrip("v")
        if not latest_version:
            raise IntelliFillError("The release feed did not include a version number.")

        assets = [
            ReleaseAsset(
                name=str(asset.get("name") or ""),
                browser_download_url=str(asset.get("browser_download_url") or ""),
                size=int(asset.get("size") or 0),
            )
            for asset in payload.get("assets", [])
        ]
        installer_asset = self._select_installer_asset(assets)

        return UpdateInfo(
            current_version=__version__,
            latest_version=latest_version,
            release_url=str(payload.get("html_url") or ""),
            release_notes=str(payload.get("body") or ""),
            is_newer=self._is_newer(latest_version, __version__),
            installer_asset=installer_asset,
            platform_label=self.platform_label(),
        )

    def download_asset(
        self,
        asset: ReleaseAsset,
        destination_dir: Path,
        progress_callback: ProgressCallback | None = None,
    ) -> Path:
        if not asset.browser_download_url:
            raise IntelliFillError("The update asset does not include a download URL.")

        destination_dir.mkdir(parents=True, exist_ok=True)
        destination = destination_dir / asset.name
        temporary_destination = destination.with_suffix(destination.suffix + ".download")

        try:
            request = Request(asset.browser_download_url, headers={"User-Agent": self.USER_AGENT})
            with urlopen(request, timeout=30) as response, temporary_destination.open("wb") as output:
                total = int(response.headers.get("Content-Length") or asset.size or 0)
                downloaded = 0
                while True:
                    chunk = response.read(1024 * 256)
                    if not chunk:
                        break
                    output.write(chunk)
                    downloaded += len(chunk)
                    if progress_callback:
                        progress_callback(downloaded, total)
            temporary_destination.replace(destination)
        except Exception as exc:  # noqa: BLE001
            if temporary_destination.exists():
                temporary_destination.unlink(missing_ok=True)
            if isinstance(exc, IntelliFillError):
                raise
            raise IntelliFillError(f"Could not download update: {exc}") from exc

        return destination

    @classmethod
    def _select_installer_asset(cls, assets: list[ReleaseAsset]) -> ReleaseAsset | None:
        platform_package = cls.platform_package_type()
        candidates = [
            ReleaseAsset(asset.name, asset.browser_download_url, asset.size, platform_package)
            for asset in assets
            if cls._asset_matches_platform(asset.name, platform_package)
        ]
        return candidates[0] if candidates else None

    @classmethod
    def _asset_matches_platform(cls, name: str, platform_package: str) -> bool:
        lower_name = name.lower()
        if platform_package == "windows":
            return lower_name.endswith(".exe") and "setup" in lower_name
        if platform_package == "debian":
            return lower_name.endswith(".deb")
        if platform_package == "fedora":
            return lower_name.endswith(".rpm")
        return False

    @classmethod
    def platform_package_type(cls) -> str:
        if sys.platform == "win32":
            return "windows"
        if sys.platform.startswith("linux"):
            distro_id = cls._linux_distro_id()
            if distro_id in {"ubuntu", "debian", "linuxmint", "pop", "elementary", "zorin"}:
                return "debian"
            if distro_id in {"fedora", "rhel", "centos", "rocky", "almalinux"}:
                return "fedora"
            if Path("/usr/bin/dnf").exists() or Path("/bin/dnf").exists():
                return "fedora"
            if Path("/usr/bin/apt").exists() or Path("/bin/apt").exists():
                return "debian"
        return "unknown"

    @classmethod
    def platform_label(cls) -> str:
        return {
            "windows": "Windows setup installer",
            "debian": "Debian/Ubuntu package",
            "fedora": "Fedora/RPM package",
        }.get(cls.platform_package_type(), "current platform")

    @staticmethod
    def install_command(asset_path: Path, package_type: str) -> str:
        quoted_path = shlex.quote(str(asset_path))
        if package_type == "debian":
            return f"sudo apt install {quoted_path}"
        if package_type == "fedora":
            return f"sudo dnf install {quoted_path}"
        if package_type == "windows":
            return str(asset_path)
        return str(asset_path)

    @staticmethod
    def _linux_distro_id() -> str:
        os_release = Path("/etc/os-release")
        if not os_release.exists():
            return ""
        try:
            for line in os_release.read_text(encoding="utf-8").splitlines():
                if line.startswith("ID="):
                    return line.split("=", 1)[1].strip().strip('"').lower()
        except OSError:
            return ""
        return ""

    @classmethod
    def _is_newer(cls, candidate: str, current: str) -> bool:
        return cls._version_tuple(candidate) > cls._version_tuple(current)

    @staticmethod
    def _version_tuple(version: str) -> tuple[int, int, int, int]:
        parts = [int(part) for part in re.findall(r"\d+", version)[:4]]
        return tuple((parts + [0, 0, 0, 0])[:4])
