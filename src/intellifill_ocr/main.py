from __future__ import annotations

import sys

from PySide6.QtWidgets import QApplication

from intellifill_ocr.database.repository import Repository
from intellifill_ocr.ui.main_window import MainWindow
from intellifill_ocr.ui.theme import apply_theme
from intellifill_ocr.utils.config import AppConfig
from intellifill_ocr.utils.logging_config import configure_logging


def main() -> int:
    config = AppConfig.load()
    configure_logging(config.log_file)
    repository = Repository(config.database_url)
    repository.create_schema()

    app = QApplication(sys.argv)
    app.setApplicationName("IntelliFill OCR")
    apply_theme(app, config.theme)

    window = MainWindow(config=config, repository=repository)
    window.resize(1500, 920)
    window.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
