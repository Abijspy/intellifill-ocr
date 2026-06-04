from __future__ import annotations

from pathlib import Path
from typing import Any

from intellifill_ocr import __version__
from intellifill_ocr.database.repository import Repository
from intellifill_ocr.ipc.serialization import (
    extracted_field_to_dict,
    mapping_to_dict,
    ocr_box_to_dict,
    parsed_document_to_dict,
    path_from_param,
    suggestion_to_dict,
    template_to_dict,
    validation_issue_to_dict,
)
from intellifill_ocr.models.document import ExtractedField, ParsedDocument, RegionSelection
from intellifill_ocr.models.template import TemplateTable
from intellifill_ocr.ocr.engine import OCREngine
from intellifill_ocr.services.document_loader import DocumentLoader
from intellifill_ocr.services.export_service import ExportService
from intellifill_ocr.services.mapping_service import MappingService
from intellifill_ocr.services.matching import FieldMatcher
from intellifill_ocr.services.preserved_template_export import PreservedTemplateExporter
from intellifill_ocr.services.template_service import TemplateService
from intellifill_ocr.services.validation import ValidationEngine
from intellifill_ocr.utils.config import AppConfig
from intellifill_ocr.utils.logging_config import configure_logging


class IpcBackend:
    """Stateful offline backend exposed to native WinUI through JSON IPC."""

    MAX_SOURCE_FILES = 5

    def __init__(self, config: AppConfig | None = None) -> None:
        self.config = config or AppConfig.load()
        configure_logging(self.config.log_file)
        self.repository = Repository(self.config.database_url)
        self.repository.create_schema()
        self.ocr_engine = OCREngine(self.config.tesseract_cmd, self.config.default_language)
        self.template_service = TemplateService(self.ocr_engine)
        self.document_loader = DocumentLoader(self.ocr_engine)
        self.matcher = FieldMatcher()
        self.mapping_service = MappingService()
        self.validation_engine = ValidationEngine()
        self.export_service = ExportService()
        self.preserved_exporter = PreservedTemplateExporter()

        self.template: TemplateTable | None = None
        self.template_source_path: Path | None = None
        self.template_id: int | None = None
        self.run_id: int | None = None
        self.sources: list[ParsedDocument] = []
        self.auto_fields: list[ExtractedField] = []
        self.manual_fields: list[ExtractedField] = []

    def handle(self, command: str, params: dict[str, Any] | None = None) -> dict[str, Any]:
        params = params or {}
        commands = {
            "system.ping": self.ping,
            "state.get": self.state,
            "state.reset": self.reset,
            "template.upload": self.upload_template,
            "template.set_cell": self.set_template_cell,
            "source.upload": self.upload_sources,
            "ocr.extract": self.extract_ocr_region,
            "mapping.suggest": self.suggest_mappings,
            "mapping.apply": self.apply_mappings,
            "validation.run": self.run_validation,
            "database.save": self.save_to_database,
            "export.create": self.create_export,
        }
        if command not in commands:
            raise ValueError(f"Unknown IPC command: {command}")
        return commands[command](params)

    def ping(self, _params: dict[str, Any]) -> dict[str, Any]:
        return {
            "app": "IntelliFill OCR",
            "version": __version__,
            "backend": "python-json-ipc",
            "database_path": str(self.config.database_path),
            "tesseract_cmd": self.config.tesseract_cmd or "",
            "language": self.config.default_language,
            "capabilities": [
                "template.upload",
                "source.upload",
                "ocr.extract",
                "mapping.suggest",
                "mapping.apply",
                "validation.run",
                "database.save",
                "export.create",
            ],
        }

    def state(self, _params: dict[str, Any]) -> dict[str, Any]:
        return {
            "template_loaded": self.template is not None,
            "template_id": self.template_id,
            "run_id": self.run_id,
            "traceability_code": self._traceability_code(),
            "template": template_to_dict(self.template) if self.template else None,
            "sources": [parsed_document_to_dict(source, index) for index, source in enumerate(self.sources)],
            "fields": self._serialized_fields(),
            "mappings": [mapping_to_dict(mapping, index) for index, mapping in enumerate(self.mapping_service.mappings)],
        }

    def reset(self, _params: dict[str, Any]) -> dict[str, Any]:
        self.template = None
        self.template_source_path = None
        self.template_id = None
        self.run_id = None
        self.sources = []
        self.auto_fields = []
        self.manual_fields = []
        self.mapping_service.clear()
        return self.state({})

    def upload_template(self, params: dict[str, Any]) -> dict[str, Any]:
        path = path_from_param(params, "path")
        self._ensure_file(path)
        self.template = self.template_service.load_template(path)
        self.template_source_path = path
        self.template_id = self.repository.save_template(self.template, path)
        self.run_id = self.repository.start_run(self.template_id)
        self.mapping_service.clear()
        return {
            "template_id": self.template_id,
            "run_id": self.run_id,
            "traceability_code": self._traceability_code(),
            "template": template_to_dict(self.template),
        }

    def set_template_cell(self, params: dict[str, Any]) -> dict[str, Any]:
        template = self._require_template()
        table_index = int(params.get("table_index") or 0)
        row = int(params.get("row") or 0)
        column = int(params.get("column") or 0)
        value = str(params.get("value") or "")
        table = self._table_at(template, table_index)
        table.set_value(row, column, value)
        return {"template": template_to_dict(template)}

    def upload_sources(self, params: dict[str, Any]) -> dict[str, Any]:
        raw_paths = params.get("paths")
        paths = raw_paths if isinstance(raw_paths, list) else [params.get("path")]
        paths = [Path(str(path)).expanduser().resolve() for path in paths if str(path or "").strip()]
        if not paths:
            raise ValueError("Upload at least one source path.")
        if len(self.sources) + len(paths) > self.MAX_SOURCE_FILES:
            raise ValueError("A maximum of 5 source files is supported per run.")

        parsed: list[ParsedDocument] = []
        for path in paths:
            self._ensure_file(path)
            document = self.document_loader.parse(path)
            self.sources.append(document)
            parsed.append(document)
            if self.run_id is not None:
                self.repository.save_uploaded_file(self.run_id, document)
        self._refresh_auto_fields()
        return {
            "sources": [parsed_document_to_dict(source, self.sources.index(source)) for source in parsed],
            "all_sources": [parsed_document_to_dict(source, index) for index, source in enumerate(self.sources)],
            "fields": self._serialized_fields(),
        }

    def extract_ocr_region(self, params: dict[str, Any]) -> dict[str, Any]:
        path = self._source_path_from_params(params)
        region_payload = params.get("region") or {}
        region = (
            int(region_payload.get("x") or params.get("x") or 0),
            int(region_payload.get("y") or params.get("y") or 0),
            int(region_payload.get("width") or params.get("width") or 0),
            int(region_payload.get("height") or params.get("height") or 0),
        )
        text, boxes = self.ocr_engine.image_to_text(path, region)
        confidence = sum(box.confidence for box in boxes) / len(boxes) if boxes else 0.0
        field = ExtractedField(
            label=str(params.get("label") or f"OCR Region {len(self.manual_fields) + 1}"),
            value=text,
            confidence=confidence,
            source_path=path,
            source_region=RegionSelection(path, int(params.get("page") or 0), *region),
        )
        self.manual_fields.append(field)
        return {
            "text": text,
            "confidence": confidence,
            "boxes": [ocr_box_to_dict(box) for box in boxes],
            "field": extracted_field_to_dict(field, len(self.fields) - 1),
            "fields": self._serialized_fields(),
        }

    def suggest_mappings(self, _params: dict[str, Any]) -> dict[str, Any]:
        template = self._require_template()
        suggestions = self.matcher.suggest(template, self.fields)
        return {
            "suggestions": [suggestion_to_dict(suggestion, index) for index, suggestion in enumerate(suggestions)],
        }

    def apply_mappings(self, params: dict[str, Any]) -> dict[str, Any]:
        template = self._require_template()
        raw_mappings = params.get("mappings")
        if not isinstance(raw_mappings, list) or not raw_mappings:
            raise ValueError("Provide one or more mappings to apply.")
        if bool(params.get("replace", True)):
            self.mapping_service.clear()

        fields = self.fields
        applied = []
        for item in raw_mappings:
            if not isinstance(item, dict):
                continue
            field = self._field_from_mapping(item, fields)
            mapping = self.mapping_service.add_mapping(
                field,
                int(item.get("target_row") or 0),
                int(item.get("target_column") or 0),
                float(item.get("confidence") or field.confidence),
                int(item.get("target_table_index") or 0),
            )
            applied.append(mapping)
            if self.run_id is not None and bool(params.get("save_to_database", True)):
                self.repository.save_mapping(
                    self.run_id,
                    mapping.source_label,
                    mapping.source_value,
                    mapping.target_row,
                    mapping.target_column,
                    mapping.confidence,
                    mapping.region,
                    target_table_index=mapping.target_table_index,
                )
        self.mapping_service.apply(template)
        return {
            "template": template_to_dict(template),
            "mappings": [mapping_to_dict(mapping, index) for index, mapping in enumerate(applied)],
        }

    def run_validation(self, _params: dict[str, Any]) -> dict[str, Any]:
        template = self._require_template()
        issues = self.validation_engine.validate(template)
        return {"issues": [validation_issue_to_dict(issue) for issue in issues]}

    def save_to_database(self, _params: dict[str, Any]) -> dict[str, Any]:
        template = self._require_template()
        if self.run_id is None:
            if self.template_id is None:
                raise ValueError("No template run exists. Upload a template first.")
            self.run_id = self.repository.start_run(self.template_id)
        self.repository.save_completed_values(self.run_id, template)
        return {"run_id": self.run_id, "traceability_code": self._traceability_code()}

    def create_export(self, params: dict[str, Any]) -> dict[str, Any]:
        template = self._require_template()
        output_path = path_from_param(params, "output_path")
        output_path.parent.mkdir(parents=True, exist_ok=True)
        export_format = str(params.get("format") or output_path.suffix.lstrip(".")).lower()
        traceability_code = str(params.get("traceability_code") or self._traceability_code())

        if export_format in {"csv"}:
            self.export_service.export_csv(template, output_path)
        elif export_format in {"xlsx", "excel"}:
            self.export_service.export_excel(template, output_path)
        elif export_format in {"docx", "word"}:
            self.export_service.export_word(template, output_path, traceability_code)
        elif export_format == "pdf":
            self.export_service.export_pdf(template, output_path, traceability_code)
        elif export_format in {"preserved", "original"}:
            if not self.template_source_path:
                raise ValueError("Original-format export requires an uploaded template path.")
            self.preserved_exporter.export(self.template_source_path, template, output_path, traceability_code)
        elif export_format in {"preserved_pdf", "original_pdf"}:
            if not self.template_source_path:
                raise ValueError("Preserved PDF export requires an uploaded template path.")
            self.export_service.export_preserved_pdf(self.template_source_path, template, output_path, traceability_code)
        else:
            raise ValueError(f"Unsupported export format: {export_format}")

        return {
            "output_path": str(output_path),
            "format": export_format,
            "traceability_code": traceability_code,
        }

    @property
    def fields(self) -> list[ExtractedField]:
        return self.auto_fields + self.manual_fields

    def _serialized_fields(self) -> list[dict[str, Any]]:
        return [extracted_field_to_dict(field, index) for index, field in enumerate(self.fields)]

    def _refresh_auto_fields(self) -> None:
        self.auto_fields = self.matcher.extract_fields(self.sources)

    def _field_from_mapping(self, item: dict[str, Any], fields: list[ExtractedField]) -> ExtractedField:
        if "source_field_id" in item:
            field_id = int(item["source_field_id"])
            if field_id < 0 or field_id >= len(fields):
                raise ValueError(f"Invalid source_field_id: {field_id}")
            return fields[field_id]
        return ExtractedField(
            label=str(item.get("source_label") or ""),
            value=str(item.get("source_value") or ""),
            confidence=float(item.get("confidence") or 0),
        )

    def _source_path_from_params(self, params: dict[str, Any]) -> Path:
        if "source_id" in params:
            source_id = int(params["source_id"])
            if source_id < 0 or source_id >= len(self.sources):
                raise ValueError(f"Invalid source_id: {source_id}")
            return self.sources[source_id].path
        path = path_from_param(params, "path")
        self._ensure_file(path)
        return path

    def _require_template(self) -> TemplateTable:
        if self.template is None:
            raise ValueError("Upload a template before using this command.")
        return self.template

    def _table_at(self, template: TemplateTable, table_index: int) -> TemplateTable:
        tables = template.all_tables()
        if table_index < 0 or table_index >= len(tables):
            raise ValueError(f"Template table index {table_index} is not available.")
        return tables[table_index]

    def _traceability_code(self) -> str:
        if self.run_id is None:
            return ""
        return self.repository.get_traceability_code(self.run_id)

    @staticmethod
    def _ensure_file(path: Path) -> None:
        if not path.exists() or not path.is_file():
            raise FileNotFoundError(f"File was not found: {path}")
