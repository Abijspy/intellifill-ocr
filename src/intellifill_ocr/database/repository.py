from __future__ import annotations

import json
from pathlib import Path

from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from intellifill_ocr.database.models import (
    Base,
    ExtractedValueRecord,
    ExtractionRunRecord,
    MappingRecord,
    TemplateRecord,
    UploadedFileRecord,
)
from intellifill_ocr.models.document import ParsedDocument, RegionSelection
from intellifill_ocr.models.template import TemplateTable
from intellifill_ocr.utils.traceability import new_traceability_code


class Repository:
    def __init__(self, database_url: str) -> None:
        self.database_url = database_url
        self.engine = create_engine(database_url, future=True)
        self.session_factory = sessionmaker(bind=self.engine, expire_on_commit=False, future=True)

    def create_schema(self) -> None:
        Base.metadata.create_all(self.engine)
        self._ensure_lightweight_migrations()

    def reconnect(self, database_url: str) -> None:
        self.engine.dispose()
        self.database_url = database_url
        self.engine = create_engine(database_url, future=True)
        self.session_factory.configure(bind=self.engine)
        self.create_schema()

    def save_template(self, template: TemplateTable, source_path: Path) -> int:
        payload = [
            [
                {
                    "row": cell.row,
                    "column": cell.column,
                    "value": cell.value,
                    "is_placeholder": cell.is_placeholder,
                    "row_span": cell.row_span,
                    "column_span": cell.column_span,
                    "source_page": cell.source_page,
                    "bbox": cell.bbox,
                }
                for cell in row
            ]
            for row in template.cells
        ]
        with self.session_factory() as session:
            record = TemplateRecord(
                name=template.name,
                source_path=str(source_path),
                table_json=json.dumps(payload),
            )
            session.add(record)
            session.commit()
            return record.id

    def start_run(self, template_id: int) -> int:
        with self.session_factory() as session:
            run = ExtractionRunRecord(template_id=template_id, traceability_code=new_traceability_code())
            session.add(run)
            session.commit()
            return run.id

    def get_traceability_code(self, run_id: int) -> str:
        with self.session_factory() as session:
            run = session.get(ExtractionRunRecord, run_id)
            return run.traceability_code if run else ""

    def save_uploaded_file(self, run_id: int, parsed: ParsedDocument) -> None:
        with self.session_factory() as session:
            session.add(
                UploadedFileRecord(
                    run_id=run_id,
                    path=str(parsed.path),
                    file_type=parsed.path.suffix.lower().lstrip("."),
                    extracted_text=parsed.text,
                )
            )
            session.commit()

    def save_mapping(
        self,
        run_id: int,
        source_label: str,
        source_value: str,
        target_row: int,
        target_column: int,
        confidence: float,
        region: RegionSelection | None = None,
    ) -> None:
        with self.session_factory() as session:
            region_json = json.dumps(region.__dict__ | {"source_path": str(region.source_path)}) if region else ""
            session.add(
                MappingRecord(
                    run_id=run_id,
                    source_label=source_label,
                    source_value=source_value,
                    target_row=target_row,
                    target_column=target_column,
                    confidence=confidence,
                    region_json=region_json,
                )
            )
            session.commit()

    def save_completed_values(self, run_id: int, template: TemplateTable) -> None:
        with self.session_factory() as session:
            for row_index, row in enumerate(template.cells):
                for col_index, cell in enumerate(row):
                    session.add(
                        ExtractedValueRecord(
                            run_id=run_id,
                            row=row_index,
                            column=col_index,
                            field_name=template.value_at(row_index, 0),
                            value=cell.value,
                            confidence=1.0 if cell.value else 0.0,
                        )
                    )
            run = session.get(ExtractionRunRecord, run_id)
            if run:
                run.status = "saved"
            session.commit()

    def session(self) -> Session:
        return self.session_factory()

    def _ensure_lightweight_migrations(self) -> None:
        with self.engine.begin() as connection:
            columns = {
                row[1]
                for row in connection.exec_driver_sql("PRAGMA table_info(extraction_runs)").fetchall()
            }
            if "traceability_code" not in columns:
                connection.exec_driver_sql("ALTER TABLE extraction_runs ADD COLUMN traceability_code VARCHAR(80) DEFAULT ''")
