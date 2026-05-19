from __future__ import annotations

import json
import os
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path

from intellifill_ocr.utils.paths import app_data_dir


@dataclass(frozen=True)
class AppConfig:
    database_path: Path
    log_file: Path
    tesseract_cmd: str | None
    default_language: str = "eng"
    theme: str = "dark"

    @property
    def database_url(self) -> str:
        return f"sqlite:///{self.database_path.as_posix()}"

    @property
    def settings_path(self) -> Path:
        return app_data_dir() / "settings.json"

    def save(self) -> None:
        payload = {
            "database_path": str(self.database_path),
            "tesseract_cmd": self.tesseract_cmd,
            "default_language": self.default_language,
            "theme": self.theme,
        }
        self.settings_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    @classmethod
    def load(cls) -> "AppConfig":
        data_dir = app_data_dir()
        settings_path = data_dir / "settings.json"
        settings: dict[str, str | None] = {}
        if settings_path.exists():
            try:
                settings = json.loads(settings_path.read_text(encoding="utf-8"))
            except json.JSONDecodeError:
                settings = {}
        configured_database = settings.get("database_path")
        configured_tesseract = settings.get("tesseract_cmd")
        tesseract_cmd = os.getenv("TESSERACT_CMD") or configured_tesseract or detect_tesseract_cmd()
        return cls(
            database_path=Path(configured_database) if configured_database else data_dir / "intellifill.sqlite3",
            log_file=data_dir / "app.log",
            tesseract_cmd=tesseract_cmd,
            default_language=os.getenv("OCR_LANG", str(settings.get("default_language") or "eng")),
            theme=os.getenv("INTELLIFILL_THEME", str(settings.get("theme") or "dark")),
        )


def detect_tesseract_cmd() -> str | None:
    """Find a local Tesseract executable without requiring internet or cloud services."""
    executable = "tesseract.exe" if sys.platform == "win32" else "tesseract"
    detected = shutil.which(executable)
    if detected:
        return detected

    candidates: list[Path]
    if sys.platform == "win32":
        candidates = [
            Path(r"C:\Program Files\Tesseract-OCR\tesseract.exe"),
            Path(r"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe"),
        ]
    else:
        candidates = [
            Path("/usr/bin/tesseract"),
            Path("/usr/local/bin/tesseract"),
            Path("/snap/bin/tesseract"),
            Path("/opt/homebrew/bin/tesseract"),
        ]

    for candidate in candidates:
        if candidate.exists():
            return str(candidate)
    return None
