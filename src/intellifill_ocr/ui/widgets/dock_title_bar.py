from __future__ import annotations

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QDockWidget, QHBoxLayout, QLabel, QToolButton, QWidget


class DockTitleBar(QWidget):
    """Theme-controlled dock title bar with reliable close and float controls."""

    def __init__(self, dock: QDockWidget, title: str) -> None:
        super().__init__(dock)
        self.dock = dock
        self.setObjectName("DockTitleBar")
        self.setAutoFillBackground(True)

        layout = QHBoxLayout(self)
        layout.setContentsMargins(8, 4, 6, 4)
        layout.setSpacing(6)

        self.title_label = QLabel(title, self)
        self.title_label.setObjectName("DockTitleLabel")
        self.title_label.setAttribute(Qt.WidgetAttribute.WA_TransparentForMouseEvents)
        self.title_label.setTextInteractionFlags(Qt.TextInteractionFlag.NoTextInteraction)
        layout.addWidget(self.title_label, 1)

        self.float_button = self._make_button("[]", "Float panel")
        self.float_button.setObjectName("DockTitleButton")
        self.float_button.clicked.connect(self._toggle_floating)
        layout.addWidget(self.float_button)

        self.close_button = self._make_button("X", "Close panel")
        self.close_button.setObjectName("DockTitleCloseButton")
        self.close_button.clicked.connect(self.dock.close)
        layout.addWidget(self.close_button)

        self.dock.topLevelChanged.connect(self._sync_float_button)
        self.dock.windowTitleChanged.connect(self.title_label.setText)
        self._sync_float_button(self.dock.isFloating())

    def _make_button(self, text: str, tooltip: str) -> QToolButton:
        button = QToolButton(self)
        button.setText(text)
        button.setToolTip(tooltip)
        button.setAccessibleName(tooltip)
        button.setFixedSize(24, 22)
        button.setAutoRaise(False)
        button.setCursor(Qt.CursorShape.PointingHandCursor)
        button.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        return button

    def _toggle_floating(self) -> None:
        self.dock.setFloating(not self.dock.isFloating())

    def _sync_float_button(self, floating: bool) -> None:
        tooltip = "Dock panel" if floating else "Float panel"
        self.float_button.setToolTip(tooltip)
        self.float_button.setAccessibleName(tooltip)

    def mouseDoubleClickEvent(self, event) -> None:  # type: ignore[override]
        if event.button() == Qt.MouseButton.LeftButton:
            self._toggle_floating()
            event.accept()
            return
        event.ignore()

    def mousePressEvent(self, event) -> None:  # type: ignore[override]
        event.ignore()

    def mouseMoveEvent(self, event) -> None:  # type: ignore[override]
        event.ignore()

    def mouseReleaseEvent(self, event) -> None:  # type: ignore[override]
        event.ignore()
