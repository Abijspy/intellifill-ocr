from __future__ import annotations

import json
import re
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


@dataclass(frozen=True)
class UpdateInfo:
    current_version: str
    latest_version: str
    release_url: str
    release_notes: str
    is_newer: bool
    installer_asset: ReleaseAsset | None


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

    @staticmethod
    def _select_installer_asset(assets: list[ReleaseAsset]) -> ReleaseAsset | None:
        setup_assets = [
            asset
            for asset in assets
            if asset.name.lower().endswith(".exe") and "setup" in asset.name.lower()
        ]
        return setup_assets[0] if setup_assets else None

    @classmethod
    def _is_newer(cls, candidate: str, current: str) -> bool:
        return cls._version_tuple(candidate) > cls._version_tuple(current)

    @staticmethod
    def _version_tuple(version: str) -> tuple[int, int, int]:
        parts = [int(part) for part in re.findall(r"\d+", version)[:3]]
        return tuple((parts + [0, 0, 0])[:3])
