from __future__ import annotations

import json
import logging
from pathlib import Path

from PySide6.QtCore import QProcess, QTimer, QUrl, Qt
from PySide6.QtGui import QAction, QDesktopServices, QIcon, QPixmap
from PySide6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QComboBox,
    QDockWidget,
    QDialog,
    QFileDialog,
    QFrame,
    QHBoxLayout,
    QLabel,
    QListWidget,
    QMainWindow,
    QMenu,
    QMessageBox,
    QProgressDialog,
    QStyle,
    QTabWidget,
    QTableWidget,
    QTableWidgetItem,
    QTextEdit,
    QToolButton,
    QVBoxLayout,
    QWidget,
)

from intellifill_ocr import __version__
from intellifill_ocr.database.repository import Repository
from intellifill_ocr.models.document import ExtractedField, ParsedDocument
from intellifill_ocr.models.template import TemplateTable
from intellifill_ocr.ocr.engine import OCREngine
from intellifill_ocr.services.document_loader import DocumentLoader
from intellifill_ocr.services.export_service import ExportService
from intellifill_ocr.services.mapping_service import MappingService
from intellifill_ocr.services.matching import FieldMatcher
from intellifill_ocr.services.preserved_template_export import PreservedTemplateExporter
from intellifill_ocr.services.template_service import TemplateService
from intellifill_ocr.services.update_service import ReleaseAsset, UpdateInfo, UpdateService
from intellifill_ocr.ui.about_dialog import AboutReleaseDialog
from intellifill_ocr.ui.barcode import barcode_pixmap
from intellifill_ocr.ui.database_preview_dialog import DatabasePreviewDialog
from intellifill_ocr.ui.log_viewer_dialog import LogViewerDialog
from intellifill_ocr.ui.settings_dialog import SettingsDialog
from intellifill_ocr.ui.theme import apply_theme
from intellifill_ocr.ui.widgets.document_viewer import DocumentViewer
from intellifill_ocr.ui.widgets.mapping_panel import MappingPanel
from intellifill_ocr.ui.widgets.template_grid import TemplateGrid
from intellifill_ocr.utils.config import AppConfig
from intellifill_ocr.utils.exceptions import IntelliFillError
from intellifill_ocr.utils.paths import app_data_dir, resource_path

LOGGER = logging.getLogger(__name__)


class MainWindow(QMainWindow):
    def __init__(self, config: AppConfig, repository: Repository) -> None:
        super().__init__()
        self.config = config
        self.repository = repository
        self.ocr_engine = OCREngine(config.tesseract_cmd, config.default_language)
        self.template_service = TemplateService(self.ocr_engine)
        self.document_loader = DocumentLoader(self.ocr_engine)
        self.matcher = FieldMatcher()
        self.mapping_service = MappingService()
        self.export_service = ExportService()
        self.preserved_exporter = PreservedTemplateExporter()
        self.update_service = UpdateService()

        self.template: TemplateTable | None = None
        self.template_path: Path | None = None
        self.template_id: int | None = None
        self.run_id: int | None = None
        self.traceability_code = ""
        self.documents: list[ParsedDocument] = []
        self.preview_documents: list[ParsedDocument] = []
        self.fields: list[ExtractedField] = []
        self.current_preview_document: ParsedDocument | None = None
        self.current_image_path: Path | None = None

        self.setWindowTitle("IntelliFill OCR - Offline Data Extraction")
        icon_path = resource_path("assets/app.ico")
        if icon_path.exists():
            self.setWindowIcon(QIcon(str(icon_path)))
        self._build_ui()
        QTimer.singleShot(900, self._show_install_changelog_if_needed)

    def _build_ui(self) -> None:
        self.setDockOptions(
            QMainWindow.DockOption.AllowNestedDocks
            | QMainWindow.DockOption.AllowTabbedDocks
            | QMainWindow.DockOption.AnimatedDocks
        )

        self.file_list = QListWidget()
        self.file_list.currentRowChanged.connect(self._show_document_text)

        self.viewer = DocumentViewer()
        self.viewer.region_selected.connect(self.ocr_selected_region)

        self.text_preview = QTextEdit()
        self.text_preview.setReadOnly(True)

        self.table_info_label = QLabel("Upload a source file or template to view parsed tables.")
        self.table_info_label.setWordWrap(True)
        self.table_selector = QComboBox()
        self.table_selector.currentIndexChanged.connect(self._show_selected_table)
        self.table_preview = QTableWidget()
        self.table_preview.setAlternatingRowColors(True)
        self.table_preview.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.table_preview.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectItems)

        self.mapping_panel = MappingPanel()
        self.template_grid = TemplateGrid()

        table_tab = QWidget()
        table_layout = QVBoxLayout(table_tab)
        table_layout.addWidget(self.table_info_label)
        table_layout.addWidget(self.table_selector)
        table_layout.addWidget(self.table_preview)

        document_tabs = QTabWidget()
        document_tabs.addTab(self.viewer, "Document Preview")
        document_tabs.addTab(self.text_preview, "Parsed Text")
        document_tabs.addTab(table_tab, "Parsed Tables")

        central = QWidget()
        central_layout = QVBoxLayout(central)
        central_layout.setContentsMargins(10, 8, 10, 8)
        central_layout.setSpacing(8)
        central_layout.addWidget(self._build_action_header())
        central_layout.addWidget(document_tabs, 1)
        self.setCentralWidget(central)

        left = QWidget()
        left_layout = QVBoxLayout(left)
        left_layout.addWidget(QLabel("Uploaded Files"))
        left_layout.addWidget(self.file_list)
        self.files_dock = QDockWidget("Uploaded Files", self)
        self.files_dock.setWidget(left)
        self.files_dock.setAllowedAreas(Qt.DockWidgetArea.LeftDockWidgetArea | Qt.DockWidgetArea.RightDockWidgetArea)
        self.addDockWidget(Qt.DockWidgetArea.LeftDockWidgetArea, self.files_dock)

        right = QWidget()
        right_layout = QVBoxLayout(right)
        right_layout.addWidget(QLabel("Extracted Fields"))
        right_layout.addWidget(self.mapping_panel)
        self.fields_dock = QDockWidget("Extracted Fields", self)
        self.fields_dock.setWidget(right)
        self.fields_dock.setAllowedAreas(Qt.DockWidgetArea.LeftDockWidgetArea | Qt.DockWidgetArea.RightDockWidgetArea)
        self.addDockWidget(Qt.DockWidgetArea.RightDockWidgetArea, self.fields_dock)

        bottom = QWidget()
        bottom_layout = QVBoxLayout(bottom)
        self.traceability_label = QLabel("Traceability ID: upload a template to create one")
        self.traceability_label.setToolTip("A traceability ID is created when a template starts a new extraction run.")
        self.barcode_label = QLabel()
        self.barcode_label.setFixedHeight(78)
        bottom_layout.addWidget(self.traceability_label)
        bottom_layout.addWidget(self.barcode_label)
        bottom_layout.addWidget(QLabel("Output Table Preview - click a destination cell, then Map Selected"))
        bottom_layout.addWidget(self.template_grid)
        self.preview_dock = QDockWidget("Output Preview", self)
        self.preview_dock.setWidget(bottom)
        self.preview_dock.setAllowedAreas(Qt.DockWidgetArea.BottomDockWidgetArea | Qt.DockWidgetArea.TopDockWidgetArea)
        self.addDockWidget(Qt.DockWidgetArea.BottomDockWidgetArea, self.preview_dock)
        self._connect_panel_actions()

    def _build_action_header(self) -> QWidget:
        header = QFrame(self)
        header.setObjectName("ActionHeader")
        layout = QHBoxLayout(header)
        layout.setContentsMargins(10, 6, 10, 6)
        layout.setSpacing(10)

        logo_label = QLabel()
        logo_label.setFixedSize(46, 46)
        logo_path = resource_path("assets/logo_512.png")
        if logo_path.exists():
            pixmap = QPixmap(str(logo_path))
            if not pixmap.isNull():
                logo_label.setPixmap(
                    pixmap.scaled(
                        44,
                        44,
                        Qt.AspectRatioMode.KeepAspectRatio,
                        Qt.TransformationMode.SmoothTransformation,
                    )
                )

        title_block = QWidget()
        title_layout = QVBoxLayout(title_block)
        title_layout.setContentsMargins(0, 0, 0, 0)
        title_layout.setSpacing(0)
        title = QLabel("IntelliFill OCR")
        title.setObjectName("AppTitle")
        subtitle = QLabel("Offline OCR, table filling, preserved exports")
        subtitle.setObjectName("AppSubtitle")
        title_layout.addWidget(title)
        title_layout.addWidget(subtitle)

        actions_button = QToolButton(self)
        actions_button.setObjectName("PrimaryActionsButton")
        actions_button.setText("Actions")
        actions_button.setToolTip("Open upload, mapping, export, tools, settings, and update actions")
        actions_button.setIcon(self.style().standardIcon(QStyle.StandardPixmap.SP_FileDialogDetailedView))
        actions_button.setToolButtonStyle(Qt.ToolButtonStyle.ToolButtonTextBesideIcon)
        actions_button.setPopupMode(QToolButton.ToolButtonPopupMode.InstantPopup)
        actions_button.setMenu(self._build_actions_menu(actions_button))

        layout.addWidget(logo_label)
        layout.addWidget(title_block)
        layout.addStretch(1)
        layout.addWidget(actions_button)
        return header

    def _build_actions_menu(self, parent: QWidget) -> QMenu:
        menu = QMenu(parent)
        menu.addAction(self._action("Upload Template", self.load_template))
        menu.addAction(self._action("Upload Source Files", self.load_sources))
        menu.addSeparator()
        menu.addAction(self._action("Auto Fill Matching Fields", self.auto_match))
        menu.addAction(self._action("Map Selected Field to Destination Cell", self.map_selected))

        mapping_menu = menu.addMenu("Saved Mapping Templates")
        mapping_menu.addAction(self._action("Save Current Field Mapping", self.save_mapping_template))
        mapping_menu.addAction(self._action("Load Saved Field Mapping", self.load_mapping_template))

        menu.addSeparator()
        menu.addAction(self._action("Save Filled Output to SQLite", self.save_to_database))

        export_menu = menu.addMenu("Export Filled Output")
        export_menu.addAction(self._action("Export CSV", self.export_csv))
        export_menu.addAction(self._action("Export Excel Workbook", self.export_excel))
        export_menu.addAction(self._action("Export Word Document", self.export_word))
        export_menu.addAction(self._action("Export PDF with Traceability Barcode", self.export_pdf))
        export_menu.addSeparator()
        export_menu.addAction(self._action("Export Filled Template - Preserve Original Layout", self.export_original_format))
        export_menu.addAction(self._action("Export Filled Template PDF - Preserve Original Layout", self.export_preserved_pdf))

        tools_menu = menu.addMenu("Tools")
        tools_menu.addAction(self._action("Preview SQLite Database", self.open_database_preview))
        tools_menu.addAction(self._action("View Application Logs", self.open_log_viewer))

        panels_menu = menu.addMenu("Panels")
        panels_menu.aboutToShow.connect(self._sync_panel_actions)
        panels_menu.addAction(self._action("Restore All Panels", self.restore_all_panels))
        panels_menu.addSeparator()
        self.files_panel_action = self._panel_action("Show Uploaded Files Panel", "files")
        self.fields_panel_action = self._panel_action("Show Extracted Fields Panel", "fields")
        self.preview_panel_action = self._panel_action("Show Output Preview Panel", "preview")
        panels_menu.addAction(self.files_panel_action)
        panels_menu.addAction(self.fields_panel_action)
        panels_menu.addAction(self.preview_panel_action)

        menu.addSeparator()
        menu.addAction(self._action("Settings", self.open_settings))

        help_menu = menu.addMenu("Help")
        help_menu.addAction(self._action("Check for Updates", self.check_for_updates))
        help_menu.addAction(self._action("What's New", self.open_about_release))
        return menu

    def _action(self, label: str, callback) -> QAction:
        action = QAction(label, self)
        action.triggered.connect(lambda _checked=False: callback())
        return action

    def _panel_action(self, label: str, panel_name: str) -> QAction:
        action = QAction(label, self)
        action.setCheckable(True)
        action.setChecked(True)
        action.triggered.connect(lambda checked=False: self.set_panel_visible(panel_name, checked))
        return action

    def _connect_panel_actions(self) -> None:
        for panel_name, action in self._panel_actions().items():
            dock = self._panel_dock(panel_name)
            if dock:
                dock.visibilityChanged.connect(lambda visible, panel_action=action: panel_action.setChecked(visible))
        self._sync_panel_actions()

    def _panel_actions(self) -> dict[str, QAction]:
        return {
            "files": self.files_panel_action,
            "fields": self.fields_panel_action,
            "preview": self.preview_panel_action,
        }

    def _panel_dock(self, panel_name: str) -> QDockWidget | None:
        return {
            "files": getattr(self, "files_dock", None),
            "fields": getattr(self, "fields_dock", None),
            "preview": getattr(self, "preview_dock", None),
        }.get(panel_name)

    def _sync_panel_actions(self) -> None:
        for panel_name, action in self._panel_actions().items():
            dock = self._panel_dock(panel_name)
            action.setChecked(bool(dock and dock.isVisible()))

    def set_panel_visible(self, panel_name: str, visible: bool) -> None:
        dock = self._panel_dock(panel_name)
        if not dock:
            return
        dock.setVisible(visible)
        if visible:
            dock.raise_()

    def restore_all_panels(self) -> None:
        for panel_name in self._panel_actions():
            self.set_panel_visible(panel_name, True)
        self._sync_panel_actions()
        self.statusBar().showMessage("Restored Uploaded Files, Extracted Fields, and Output Preview panels", 5000)

    def load_template(self) -> None:
        path_str, _ = QFileDialog.getOpenFileName(
            self,
            "Upload Template",
            "",
            "Templates (*.xlsx *.xls *.csv *.docx *.pdf *.png *.jpg *.jpeg)",
        )
        if not path_str:
            return
        try:
            path = Path(path_str)
            self._reset_workflow_for_new_template()
            self.template = self.template_service.load_template(path)
            self.template_path = path
            self.template_id = self.repository.save_template(self.template, path)
            self.run_id = self.repository.start_run(self.template_id)
            self.traceability_code = self.repository.get_traceability_code(self.run_id)
            self._update_traceability_preview()
            self.template_grid.load_template(self.template)
            self._add_preview_document(path, "Template")
            self.statusBar().showMessage(f"Loaded template: {path.name}", 5000)
        except Exception as exc:  # noqa: BLE001
            self._show_error("Template load failed", exc)

    def load_sources(self) -> None:
        paths, _ = QFileDialog.getOpenFileNames(
            self,
            "Upload up to 5 Source Files",
            "",
            "Source files (*.docx *.xlsx *.xls *.csv *.png *.jpg *.jpeg *.pdf)",
        )
        if not paths:
            return
        if len(paths) > 5:
            QMessageBox.warning(self, "Too many files", "Please select no more than 5 source files.")
            return
        try:
            for path_str in paths:
                parsed = self.document_loader.parse(Path(path_str))
                self.documents.append(parsed)
                self.preview_documents.append(parsed)
                self.file_list.addItem(f"Source: {parsed.path.name}")
                if self.run_id:
                    self.repository.save_uploaded_file(self.run_id, parsed)
            self.fields = self.matcher.extract_fields(self.documents)
            self.mapping_panel.set_fields(self.fields)
            self.file_list.setCurrentRow(len(self.preview_documents) - 1)
            self._show_document_text(len(self.preview_documents) - 1)
            self.statusBar().showMessage(f"Loaded {len(paths)} source file(s)", 5000)
        except Exception as exc:  # noqa: BLE001
            self._show_error("Source load failed", exc)

    def auto_match(self) -> None:
        if not self.template:
            QMessageBox.information(self, "Template required", "Upload a template first.")
            return
        suggestions = self.matcher.suggest(self.template, self.fields)
        self.mapping_service.clear()
        for suggestion in suggestions:
            field = ExtractedField(suggestion.source_label, suggestion.source_value, suggestion.confidence)
            mapping = self.mapping_service.add_mapping(field, suggestion.target_row, suggestion.target_column, suggestion.confidence)
            if self.run_id:
                self.repository.save_mapping(
                    self.run_id,
                    mapping.source_label,
                    mapping.source_value,
                    mapping.target_row,
                    mapping.target_column,
                    mapping.confidence,
                    mapping.region,
                )
        self.mapping_service.apply(self.template)
        self.template_grid.update_from_template()
        self.statusBar().showMessage(f"Applied {len(suggestions)} intelligent match suggestion(s)", 5000)

    def map_selected(self) -> None:
        if not self.template:
            QMessageBox.information(self, "Template required", "Upload a template first.")
            return
        field = self.mapping_panel.current_field()
        destination = self.template_grid.current_destination()
        if not field or not destination:
            QMessageBox.information(self, "Selection required", "Select an extracted field and a destination cell.")
            return
        row, column = destination
        mapping = self.mapping_service.add_mapping(field, row, column)
        self.template.set_value(row, column, mapping.source_value)
        self.template_grid.update_from_template()
        if self.run_id:
            self.repository.save_mapping(
                self.run_id,
                mapping.source_label,
                mapping.source_value,
                mapping.target_row,
                mapping.target_column,
                mapping.confidence,
                mapping.region,
            )

    def ocr_selected_region(self, x: int, y: int, width: int, height: int) -> None:
        if not self.current_image_path:
            return
        try:
            text, boxes = self.ocr_engine.image_to_text(self.current_image_path, (x, y, width, height))
            field = ExtractedField(label="OCR Region", value=text, confidence=self._average_confidence(boxes), source_path=self.current_image_path)
            self.fields.append(field)
            self.mapping_panel.set_fields(self.fields)
            self.viewer.show_boxes(boxes)
            self.statusBar().showMessage("Region OCR complete. Select the new OCR Region field to map it.", 6000)
        except Exception as exc:  # noqa: BLE001
            self._show_error("Region OCR failed", exc)

    def save_to_database(self) -> None:
        if not self.template or not self.run_id:
            QMessageBox.information(self, "Nothing to save", "Load a template and create mappings first.")
            return
        self.repository.save_completed_values(self.run_id, self.template)
        QMessageBox.information(self, "Saved", "Final table and mappings were saved to SQLite.")

    def save_mapping_template(self) -> None:
        path_str, _ = QFileDialog.getSaveFileName(self, "Save Mapping Template", "mapping.json", "JSON (*.json)")
        if not path_str:
            return
        payload = [mapping.__dict__ | {"region": None} for mapping in self.mapping_service.mappings]
        Path(path_str).write_text(json.dumps(payload, indent=2), encoding="utf-8")
        self.statusBar().showMessage("Mapping template saved", 5000)

    def load_mapping_template(self) -> None:
        if not self.template:
            QMessageBox.information(self, "Template required", "Upload a template before loading mappings.")
            return
        path_str, _ = QFileDialog.getOpenFileName(self, "Load Mapping Template", "", "JSON (*.json)")
        if not path_str:
            return
        try:
            payload = json.loads(Path(path_str).read_text(encoding="utf-8"))
            self.mapping_service.clear()
            for item in payload:
                field = ExtractedField(
                    label=item["source_label"],
                    value=item["source_value"],
                    confidence=float(item.get("confidence", 0)),
                )
                self.mapping_service.add_mapping(field, int(item["target_row"]), int(item["target_column"]))
            self.mapping_service.apply(self.template)
            self.template_grid.update_from_template()
            self.statusBar().showMessage("Mapping template loaded", 5000)
        except Exception as exc:  # noqa: BLE001
            self._show_error("Load mapping failed", exc)

    def export_csv(self) -> None:
        self._export("csv")

    def export_excel(self) -> None:
        self._export("xlsx")

    def export_word(self) -> None:
        self._export("docx")

    def export_pdf(self) -> None:
        self._export("pdf")

    def export_original_format(self) -> None:
        if not self.template or not self.template_path:
            QMessageBox.information(self, "Template required", "Upload and fill a template first.")
            return
        suffix = self.template_path.suffix.lower()
        if suffix not in {".docx", ".xlsx", ".csv"}:
            QMessageBox.information(
                self,
                "Original export not available",
                "Layout-preserving export is available for DOCX, XLSX, and CSV templates. Use PDF export for other templates.",
            )
            return
        default_name = f"{self.template_path.stem}_filled{suffix}"
        path_str, _ = QFileDialog.getSaveFileName(self, "Export Filled Template", default_name, f"*{suffix}")
        if not path_str:
            return
        try:
            self.preserved_exporter.export(self.template_path, self.template, Path(path_str), self.traceability_code)
            self.statusBar().showMessage("Exported filled template while preserving original layout", 6000)
        except Exception as exc:  # noqa: BLE001
            self._show_error("Original-format export failed", exc)

    def export_preserved_pdf(self) -> None:
        if not self.template or not self.template_path:
            QMessageBox.information(self, "Template required", "Upload and fill a template first.")
            return
        default_name = f"{self.template_path.stem}_filled.pdf"
        path_str, _ = QFileDialog.getSaveFileName(self, "Export Filled Template PDF", default_name, "PDF (*.pdf)")
        if not path_str:
            return
        try:
            self.export_service.export_preserved_pdf(
                self.template_path,
                self.template,
                Path(path_str),
                self.traceability_code,
            )
            self.statusBar().showMessage("Exported filled PDF with one traceability barcode", 6000)
        except Exception as exc:  # noqa: BLE001
            self._show_error("Preserved PDF export failed", exc)

    def open_settings(self) -> None:
        dialog = SettingsDialog(self.config, self)
        if dialog.exec() != QDialog.DialogCode.Accepted:
            return

        new_config = dialog.selected_config(self.config)
        database_changed = new_config.database_path != self.config.database_path
        theme_changed = new_config.theme != self.config.theme
        ocr_changed = (
            new_config.tesseract_cmd != self.config.tesseract_cmd
            or new_config.default_language != self.config.default_language
        )
        self.config = new_config
        self.config.save()

        if ocr_changed:
            self.ocr_engine = OCREngine(self.config.tesseract_cmd, self.config.default_language)
            self.template_service = TemplateService(self.ocr_engine)
            self.document_loader = DocumentLoader(self.ocr_engine)
        if database_changed:
            self.repository.reconnect(self.config.database_url)
            self.template_id = None
            self.run_id = None
            if self.template:
                self.template_id = self.repository.save_template(self.template, self.template_path or self.config.database_path)
                self.run_id = self.repository.start_run(self.template_id)
                self.traceability_code = self.repository.get_traceability_code(self.run_id)
                self._update_traceability_preview()
            for document in self.documents:
                if self.run_id:
                    self.repository.save_uploaded_file(self.run_id, document)
        if theme_changed:
            app = QApplication.instance()
            if app:
                apply_theme(app, self.config.theme)

        self.statusBar().showMessage("Settings saved", 5000)

    def open_database_preview(self) -> None:
        try:
            self.repository.create_schema()
        except Exception as exc:  # noqa: BLE001
            self._show_error("Database preview failed", exc)
            return
        dialog = DatabasePreviewDialog(self.config.database_path, self)
        dialog.exec()

    def open_log_viewer(self) -> None:
        dialog = LogViewerDialog(self.config.log_file, self)
        dialog.exec()

    def open_about_release(self) -> None:
        dialog = AboutReleaseDialog(self)
        dialog.exec()

    def _show_install_changelog_if_needed(self) -> None:
        state_path = app_data_dir() / "last_seen_release_version.txt"
        try:
            seen_version = state_path.read_text(encoding="utf-8").strip() if state_path.exists() else ""
            if seen_version == __version__:
                return
            dialog = AboutReleaseDialog(self)
            dialog.exec()
            state_path.write_text(__version__, encoding="utf-8")
        except Exception:
            LOGGER.exception("Could not show install/update changelog")

    def check_for_updates(self) -> None:
        QApplication.setOverrideCursor(Qt.CursorShape.WaitCursor)
        try:
            update_info = self.update_service.fetch_latest()
        except Exception as exc:  # noqa: BLE001
            QApplication.restoreOverrideCursor()
            self._show_error("Update check failed", exc)
            return
        finally:
            if QApplication.overrideCursor():
                QApplication.restoreOverrideCursor()

        if not update_info.is_newer:
            QMessageBox.information(
                self,
                "No Update Available",
                f"You are running IntelliFill OCR {update_info.current_version}.\n"
                f"The latest release is {update_info.latest_version}.",
            )
            return

        if not update_info.installer_asset:
            self._show_update_without_installer(update_info)
            return

        answer = QMessageBox.question(
            self,
            "Update Available",
            f"IntelliFill OCR {update_info.latest_version} is available.\n"
            f"Current version: {update_info.current_version}\n\n"
            "Download and launch the installer now?",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if answer != QMessageBox.StandardButton.Yes:
            return

        installer_path = self._download_update_installer(update_info.installer_asset)
        if not installer_path:
            return

        if not QProcess.startDetached(str(installer_path), []):
            QMessageBox.warning(self, "Installer launch failed", f"Could not start installer:\n{installer_path}")
            return

        QMessageBox.information(
            self,
            "Installer Started",
            "The update installer has started. IntelliFill OCR will close now so the installer can replace files.",
        )
        QTimer.singleShot(500, QApplication.quit)

    def _download_update_installer(self, asset: ReleaseAsset) -> Path | None:
        progress = QProgressDialog("Downloading update installer...", "Cancel", 0, 100, self)
        progress.setWindowTitle("Downloading Update")
        progress.setWindowModality(Qt.WindowModality.WindowModal)
        progress.setMinimumDuration(0)

        def on_progress(downloaded: int, total: int) -> None:
            if progress.wasCanceled():
                raise IntelliFillError("Update download was canceled.")
            if total > 0:
                progress.setValue(min(100, int(downloaded * 100 / total)))
            else:
                progress.setValue(0)
            QApplication.processEvents()

        try:
            installer_path = self.update_service.download_asset(
                asset,
                app_data_dir() / "updates",
                on_progress,
            )
            progress.setValue(100)
            return installer_path
        except Exception as exc:  # noqa: BLE001
            self._show_error("Update download failed", exc)
            return None
        finally:
            progress.close()

    def _show_update_without_installer(self, update_info: UpdateInfo) -> None:
        answer = QMessageBox.question(
            self,
            "Update Available",
            f"IntelliFill OCR {update_info.latest_version} is available, but no setup installer was attached.\n\n"
            "Open the release page in your browser?",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
        )
        if answer == QMessageBox.StandardButton.Yes and update_info.release_url:
            QDesktopServices.openUrl(QUrl(update_info.release_url))

    def _export(self, suffix: str) -> None:
        if not self.template:
            QMessageBox.information(self, "Nothing to export", "Load and fill a template first.")
            return
        path_str, _ = QFileDialog.getSaveFileName(self, "Export Output", f"output.{suffix}", f"*.{suffix}")
        if not path_str:
            return
        path = Path(path_str)
        if suffix == "csv":
            self.export_service.export_csv(self.template, path)
        elif suffix == "xlsx":
            self.export_service.export_excel(self.template, path)
        elif suffix == "docx":
            self.export_service.export_word(self.template, path, self.traceability_code)
        else:
            self.export_service.export_pdf(self.template, path, self.traceability_code)
        self.statusBar().showMessage(f"Exported {path.name}", 5000)

    def _show_document_text(self, index: int) -> None:
        if not (0 <= index < len(self.preview_documents)):
            return
        document = self.preview_documents[index]
        self.current_preview_document = document
        self.text_preview.setPlainText(document.text)
        self._load_document_tables(document)
        if document.path.suffix.lower() in {".png", ".jpg", ".jpeg"}:
            self.current_image_path = document.path
            self.viewer.load_image(document.path)
            self.viewer.show_boxes(document.ocr_boxes)
        elif document.path.suffix.lower() == ".pdf":
            self.current_image_path = self._render_pdf_preview(document.path)
            if self.current_image_path:
                self.viewer.load_image(self.current_image_path)
        else:
            self.current_image_path = None
            self.viewer.scene.clear()
            self.viewer.scene.addText("Document preview is available in the Parsed Text tab.")

    def _reset_workflow_for_new_template(self) -> None:
        self.documents.clear()
        self.preview_documents.clear()
        self.fields.clear()
        self.mapping_service.clear()
        self.file_list.clear()
        self.mapping_panel.set_fields([])
        self.traceability_code = ""
        self.current_image_path = None
        self.viewer.scene.clear()
        self.text_preview.clear()
        self.current_preview_document = None
        self._clear_table_preview("Upload a source file or template to view parsed tables.")

    def _add_preview_document(self, path: Path, label: str) -> None:
        try:
            parsed = self.document_loader.parse(path)
        except Exception:
            LOGGER.exception("Could not build preview for %s", path)
            parsed = ParsedDocument(path=path, text=f"{label} loaded: {path.name}")
        self.preview_documents.append(parsed)
        self.file_list.addItem(f"{label}: {path.name}")
        self.file_list.setCurrentRow(len(self.preview_documents) - 1)

    def _update_traceability_preview(self) -> None:
        if not self.traceability_code:
            self.traceability_label.setText("Traceability ID: upload a template to create one")
            self.traceability_label.setToolTip(
                "A traceability ID is created when a template starts a new extraction run."
            )
            self.barcode_label.clear()
            return
        self.traceability_label.setText(
            f"Traceability ID: {self.traceability_code} (bottom-center barcode on PDF and Word exports)"
        )
        self.traceability_label.setToolTip(
            "This compact ID is stored with the SQLite extraction run and printed as a bottom-center barcode on exports."
        )
        self.barcode_label.setPixmap(barcode_pixmap(self.traceability_code))
        self.barcode_label.setToolTip("Scan or read this barcode to match an exported document back to the saved SQLite run.")

    def _load_document_tables(self, document: ParsedDocument) -> None:
        self.table_selector.blockSignals(True)
        self.table_selector.clear()
        for table_index, table in enumerate(document.tables):
            rows = len(table)
            columns = max((len(row) for row in table), default=0)
            self.table_selector.addItem(f"Table {table_index + 1}: {rows} rows x {columns} columns")
        self.table_selector.setEnabled(len(document.tables) > 1)
        self.table_selector.blockSignals(False)

        if document.tables:
            self.table_info_label.setText(f"Parsed tables from {document.path.name}")
            self._render_table_preview(document.tables[0])
            return

        document_fields = [
            field
            for field in self.fields
            if field.source_path and field.source_path.resolve() == document.path.resolve()
        ]
        if document_fields:
            self.table_info_label.setText(
                f"No structured table was detected in {document.path.name}. Showing extracted key/value fields instead."
            )
            rows = [["Field", "Value", "Confidence"]]
            rows.extend([[field.label, field.value, f"{field.confidence:.0f}%"] for field in document_fields])
            self._render_table_preview(rows, first_row_is_header=True)
            return

        self._clear_table_preview(f"No parsed table was detected in {document.path.name}. See Parsed Text instead.")

    def _show_selected_table(self, index: int) -> None:
        if not self.current_preview_document or not (0 <= index < len(self.current_preview_document.tables)):
            return
        self._render_table_preview(self.current_preview_document.tables[index])

    def _render_table_preview(self, table: list[list[str]], first_row_is_header: bool = False) -> None:
        if not table:
            self._clear_table_preview("No table rows were detected.")
            return

        rows = table[1:] if first_row_is_header else table
        headers = table[0] if first_row_is_header else []
        column_count = max((len(row) for row in rows), default=len(headers))
        self.table_preview.clear()
        self.table_preview.setRowCount(len(rows))
        self.table_preview.setColumnCount(column_count)
        if first_row_is_header:
            self.table_preview.setHorizontalHeaderLabels(headers + [""] * max(column_count - len(headers), 0))
        else:
            self.table_preview.setHorizontalHeaderLabels([f"Column {column + 1}" for column in range(column_count)])
        self.table_preview.setVerticalHeaderLabels([str(row + 1) for row in range(len(rows))])

        for row_index, row in enumerate(rows):
            for column_index in range(column_count):
                value = row[column_index] if column_index < len(row) else ""
                item = QTableWidgetItem(str(value))
                item.setToolTip(str(value))
                self.table_preview.setItem(row_index, column_index, item)

        if len(rows) * max(column_count, 1) <= 1200:
            self.table_preview.resizeColumnsToContents()
            self.table_preview.resizeRowsToContents()

    def _clear_table_preview(self, message: str) -> None:
        self.table_info_label.setText(message)
        self.table_selector.blockSignals(True)
        self.table_selector.clear()
        self.table_selector.setEnabled(False)
        self.table_selector.blockSignals(False)
        self.table_preview.clear()
        self.table_preview.setRowCount(0)
        self.table_preview.setColumnCount(0)

    def _render_pdf_preview(self, path: Path) -> Path | None:
        try:
            import pypdfium2 as pdfium

            preview_dir = app_data_dir() / "previews"
            preview_dir.mkdir(parents=True, exist_ok=True)
            image_path = preview_dir / f"{path.stem}_{int(path.stat().st_mtime)}.png"
            if not image_path.exists():
                pdf = pdfium.PdfDocument(str(path))
                if len(pdf) == 0:
                    return None
                bitmap = pdf[0].render(scale=1.6).to_pil()
                bitmap.save(image_path)
            return image_path
        except Exception:
            LOGGER.exception("PDF preview failed for %s", path)
            return None

    def _average_confidence(self, boxes) -> float:
        if not boxes:
            return 0.0
        return sum(box.confidence for box in boxes) / len(boxes)

    def _show_error(self, title: str, exc: Exception) -> None:
        LOGGER.exception(title)
        message = str(exc)
        if isinstance(exc, IntelliFillError):
            QMessageBox.warning(self, title, message)
        else:
            QMessageBox.critical(self, title, message)
