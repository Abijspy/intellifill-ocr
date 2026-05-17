from __future__ import annotations

from PySide6.QtWidgets import QApplication


LIGHT_STYLE = """
QWidget {
    font-family: Segoe UI;
    font-size: 10pt;
    color: #172033;
    background: #f6f8fb;
    selection-background-color: #d9e9ff;
    selection-color: #0f172a;
}
QMainWindow, QDialog, QTabWidget::pane {
    background: #f6f8fb;
}
QFrame#ActionHeader {
    background: #ffffff;
    border: 1px solid #d9dee8;
    border-radius: 8px;
}
QLabel#AppTitle {
    color: #0f172a;
    font-size: 15pt;
    font-weight: 700;
    background: transparent;
}
QLabel#AppSubtitle {
    color: #516070;
    font-size: 9pt;
    background: transparent;
}
QMenuBar {
    background: #ffffff;
    color: #172033;
    border-bottom: 1px solid #d9dee8;
}
QMenuBar::item {
    padding: 6px 10px;
    background: transparent;
}
QMenuBar::item:selected, QMenu::item:selected {
    background: #e8f1ff;
}
QMenu {
    background: #ffffff;
    color: #172033;
    border: 1px solid #cfd7e4;
}
QMenu::item {
    padding: 7px 28px 7px 18px;
}
QToolBar {
    background: #ffffff;
    border: 0;
    border-bottom: 1px solid #d9dee8;
    spacing: 6px;
    padding: 6px;
}
QToolButton, QPushButton {
    color: #172033;
    padding: 7px 10px;
    border: 1px solid #c7d0dc;
    border-radius: 6px;
    background: #ffffff;
}
QToolButton:hover, QPushButton:hover {
    background: #eef5ff;
    border-color: #8db8f7;
}
QToolButton:pressed, QPushButton:pressed {
    background: #dfeeff;
}
QToolButton#PrimaryActionsButton {
    background: #0f62fe;
    border-color: #0f62fe;
    color: #ffffff;
    font-weight: 600;
    min-height: 30px;
    padding: 8px 16px;
}
QToolButton#PrimaryActionsButton:hover {
    background: #0b55d9;
    border-color: #0b55d9;
}
QToolButton#PrimaryActionsButton::menu-indicator {
    image: none;
}
QDockWidget::title {
    background: #edf1f7;
    color: #172033;
    padding: 7px 44px 7px 8px;
    border: 1px solid #d9dee8;
}
QDockWidget::close-button, QDockWidget::float-button {
    background: #ffffff;
    border: 1px solid #c7d0dc;
    border-radius: 3px;
    width: 16px;
    height: 16px;
}
QDockWidget::close-button:hover, QDockWidget::float-button:hover {
    background: #d9e9ff;
    border-color: #8db8f7;
}
QTableWidget, QTextEdit, QListWidget, QTreeWidget, QLineEdit, QComboBox {
    background: #ffffff;
    color: #172033;
    border: 1px solid #cfd7e4;
    border-radius: 4px;
}
QTableWidget {
    gridline-color: #dce3ed;
    alternate-background-color: #f8fafc;
}
QTableWidget::item:selected, QListWidget::item:selected {
    background: #d9e9ff;
    color: #0f172a;
}
QHeaderView::section {
    background: #edf1f7;
    color: #172033;
    padding: 6px;
    border: 0;
    border-right: 1px solid #d9dee8;
    border-bottom: 1px solid #d9dee8;
}
QTabBar::tab {
    background: #e8edf5;
    color: #243044;
    padding: 8px 14px;
    border: 1px solid #d1d8e5;
    border-bottom: 0;
    border-top-left-radius: 5px;
    border-top-right-radius: 5px;
}
QTabBar::tab:selected {
    background: #ffffff;
    color: #0f172a;
}
QStatusBar {
    background: #ffffff;
    color: #475569;
    border-top: 1px solid #d9dee8;
}
QGraphicsView {
    background: #eef2f7;
    border: 1px solid #cfd7e4;
}
QScrollBar:vertical, QScrollBar:horizontal {
    background: #edf1f7;
    border: 0;
}
QScrollBar::handle:vertical, QScrollBar::handle:horizontal {
    background: #b9c4d3;
    border-radius: 4px;
    min-height: 24px;
    min-width: 24px;
}
QScrollBar::handle:hover {
    background: #8fa0b7;
}
QScrollBar::add-line, QScrollBar::sub-line {
    width: 0;
    height: 0;
}
"""

DARK_STYLE = """
QWidget { font-family: Segoe UI; font-size: 10pt; color: #edf2f7; background: #151922; selection-background-color: #334b70; }
QMainWindow, QDialog, QTabWidget::pane { background: #151922; }
QFrame#ActionHeader { background: #1d2430; border: 1px solid #303948; border-radius: 8px; }
QLabel#AppTitle { color: #ffffff; font-size: 15pt; font-weight: 700; background: transparent; }
QLabel#AppSubtitle { color: #aab6c7; font-size: 9pt; background: transparent; }
QMenuBar, QMenu, QToolBar, QStatusBar { background: #1d2430; color: #edf2f7; border-color: #303948; }
QMenuBar::item { padding: 6px 10px; background: transparent; }
QMenuBar::item:selected, QMenu::item:selected { background: #31405a; }
QMenu::item { padding: 7px 28px 7px 18px; }
QToolBar { border-bottom: 1px solid #303948; spacing: 6px; padding: 6px; }
QTableWidget, QTextEdit, QListWidget, QTreeWidget {
    background: #1d2430; border: 1px solid #303948; gridline-color: #3c4657;
}
QToolButton, QPushButton { padding: 7px 10px; border: 1px solid #445064; border-radius: 6px; background: #252d3a; color: #edf2f7; }
QToolButton:hover, QPushButton:hover { background: #31405a; }
QToolButton#PrimaryActionsButton { background: #60a5fa; border-color: #60a5fa; color: #0f172a; font-weight: 700; min-height: 30px; padding: 8px 16px; }
QToolButton#PrimaryActionsButton:hover { background: #93c5fd; border-color: #93c5fd; }
QToolButton#PrimaryActionsButton::menu-indicator { image: none; }
QLineEdit, QComboBox { background: #1d2430; border: 1px solid #445064; padding: 5px; }
QHeaderView::section { background: #252d3a; padding: 6px; border: 0; color: #edf2f7; }
QDockWidget::title { background: #252d3a; color: #edf2f7; padding: 7px 44px 7px 8px; border: 1px solid #303948; }
QDockWidget::close-button, QDockWidget::float-button { background: #1d2430; border: 1px solid #64748b; border-radius: 3px; width: 16px; height: 16px; }
QDockWidget::close-button:hover, QDockWidget::float-button:hover { background: #31405a; border-color: #93c5fd; }
QTableWidget::item:selected, QListWidget::item:selected { background: #334b70; color: #ffffff; }
QTabBar::tab { background: #252d3a; color: #cbd5e1; padding: 8px 14px; border: 1px solid #303948; border-bottom: 0; border-top-left-radius: 5px; border-top-right-radius: 5px; }
QTabBar::tab:selected { background: #1d2430; color: #ffffff; }
QGraphicsView { background: #10141c; border: 1px solid #303948; }
QSplitter::handle { background: #303948; }
"""


def apply_theme(app: QApplication, theme: str) -> None:
    app.setStyleSheet(DARK_STYLE if theme.lower() == "dark" else LIGHT_STYLE)
