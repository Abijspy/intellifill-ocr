from __future__ import annotations

import json
import os
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
        return cls(
            database_path=Path(configured_database) if configured_database else data_dir / "intellifill.sqlite3",
            log_file=data_dir / "app.log",
            tesseract_cmd=os.getenv("TESSERACT_CMD") or configured_tesseract,
            default_language=os.getenv("OCR_LANG", str(settings.get("default_language") or "eng")),
            theme=os.getenv("INTELLIFILL_THEME", str(settings.get("theme") or "dark")),
        )
