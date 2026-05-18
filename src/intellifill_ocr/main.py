from __future__ import annotations

import ctypes
import sys

from PySide6.QtGui import QIcon
from PySide6.QtWidgets import QApplication

from intellifill_ocr import __version__
from intellifill_ocr.database.repository import Repository
from intellifill_ocr.ui.main_window import MainWindow
from intellifill_ocr.ui.smooth_scroll import install_smooth_scrolling
from intellifill_ocr.ui.theme import apply_theme
from intellifill_ocr.utils.config import AppConfig
from intellifill_ocr.utils.logging_config import configure_logging
from intellifill_ocr.utils.paths import resource_path


WINDOWS_APP_USER_MODEL_ID = "IntelliFillOCR.Desktop"


def configure_windows_taskbar_identity() -> None:
    if sys.platform != "win32":
        return
    try:
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(WINDOWS_APP_USER_MODEL_ID)
    except Exception:
        pass


def main() -> int:
    configure_windows_taskbar_identity()
    config = AppConfig.load()
    configure_logging(config.log_file)
    repository = Repository(config.database_url)
    repository.create_schema()

    app = QApplication(sys.argv)
    app.setApplicationName("IntelliFill OCR")
    app.setApplicationVersion(__version__)
    app.setOrganizationName("IntelliFill OCR")
    icon_path = resource_path("assets/app.ico")
    if icon_path.exists():
        app.setWindowIcon(QIcon(str(icon_path)))
    apply_theme(app, config.theme)
    install_smooth_scrolling(app)

    window = MainWindow(config=config, repository=repository)
    window.resize(1500, 920)
    window.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
