from __future__ import annotations

from PySide6.QtCore import QTimer
from PySide6.QtGui import QGuiApplication
from PySide6.QtWidgets import QDialog


def keep_dialog_on_screen(dialog: QDialog, preferred_width: int, preferred_height: int) -> None:
    """Resize and center a dialog inside the usable desktop area.

    Windows can restore large dialogs partly outside the visible work area on
    smaller displays or after monitor changes. Running once immediately and once
    after the native window is created keeps the title bar and close button in
    reach.
    """

    dialog.setSizeGripEnabled(True)

    def fit() -> None:
        screen = dialog.screen() or QGuiApplication.primaryScreen()
        if not screen:
            dialog.resize(preferred_width, preferred_height)
            return

        available = screen.availableGeometry()
        margin = 24
        max_width = max(420, available.width() - margin * 2)
        max_height = max(320, available.height() - margin * 2)
        width = min(preferred_width, max_width)
        height = min(preferred_height, max_height)
        dialog.resize(width, height)

        x = available.left() + max(margin, int((available.width() - width) / 2))
        y = available.top() + max(margin, int((available.height() - height) / 2))
        x = min(x, available.right() - width - margin)
        y = min(y, available.bottom() - height - margin)
        dialog.move(max(available.left() + margin, x), max(available.top() + margin, y))

    fit()
    QTimer.singleShot(0, fit)
