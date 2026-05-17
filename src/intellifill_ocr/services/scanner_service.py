from __future__ import annotations

import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from intellifill_ocr.utils.exceptions import IntelliFillError
from intellifill_ocr.utils.paths import app_data_dir


@dataclass(frozen=True)
class ScanResult:
    image_path: Path
    device_hint: str = "WIA scanner"


class ScannerService:
    """Uses Windows WIA through PowerShell COM to acquire scans offline."""

    def is_available(self) -> bool:
        return sys.platform == "win32"

    def acquire_image(self) -> ScanResult:
        if not self.is_available():
            raise IntelliFillError("Direct scanner integration is available on Windows with WIA/TWAIN scanner drivers.")

        output_dir = app_data_dir() / "scans"
        output_dir.mkdir(parents=True, exist_ok=True)
        image_path = output_dir / f"scan_{datetime.now().strftime('%Y%m%d_%H%M%S')}.png"
        script = self._wia_script(image_path)
        completed = subprocess.run(
            [
                "powershell",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-Command",
                script,
            ],
            capture_output=True,
            text=True,
            timeout=180,
            check=False,
        )
        if completed.returncode != 0:
            message = (completed.stderr or completed.stdout or "Scanner dialog was canceled or no scanner was found.").strip()
            raise IntelliFillError(message)
        if not image_path.exists():
            raise IntelliFillError("Scanner did not create an image file. Check the scanner driver and try again.")
        return ScanResult(image_path=image_path)

    def _wia_script(self, output_path: Path) -> str:
        escaped = str(output_path).replace("'", "''")
        return (
            "$ErrorActionPreference = 'Stop'; "
            f"$path = '{escaped}'; "
            "if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }; "
            "$dialog = New-Object -ComObject WIA.CommonDialog; "
            "$image = $dialog.ShowAcquireImage(); "
            "if ($null -eq $image) { throw 'No image was acquired from the scanner.' }; "
            "$image.SaveFile($path); "
            "Write-Output $path"
        )
