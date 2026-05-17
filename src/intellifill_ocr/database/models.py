from __future__ import annotations

from datetime import datetime

from sqlalchemy import DateTime, Float, ForeignKey, Integer, String, Text
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship


class Base(DeclarativeBase):
    pass


class TemplateRecord(Base):
    __tablename__ = "templates"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    name: Mapped[str] = mapped_column(String(255), nullable=False)
    source_path: Mapped[str] = mapped_column(Text, nullable=False)
    table_json: Mapped[str] = mapped_column(Text, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow)

    runs: Mapped[list["ExtractionRunRecord"]] = relationship(back_populates="template")


class ExtractionRunRecord(Base):
    __tablename__ = "extraction_runs"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    template_id: Mapped[int] = mapped_column(ForeignKey("templates.id"), nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow)
    status: Mapped[str] = mapped_column(String(50), default="draft")
    traceability_code: Mapped[str] = mapped_column(String(80), default="")

    template: Mapped[TemplateRecord] = relationship(back_populates="runs")
    files: Mapped[list["UploadedFileRecord"]] = relationship(back_populates="run", cascade="all, delete-orphan")
    values: Mapped[list["ExtractedValueRecord"]] = relationship(back_populates="run", cascade="all, delete-orphan")
    mappings: Mapped[list["MappingRecord"]] = relationship(back_populates="run", cascade="all, delete-orphan")


class UploadedFileRecord(Base):
    __tablename__ = "uploaded_files"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    run_id: Mapped[int] = mapped_column(ForeignKey("extraction_runs.id"), nullable=False)
    path: Mapped[str] = mapped_column(Text, nullable=False)
    file_type: Mapped[str] = mapped_column(String(30), nullable=False)
    extracted_text: Mapped[str] = mapped_column(Text, default="")
    created_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow)

    run: Mapped[ExtractionRunRecord] = relationship(back_populates="files")


class MappingRecord(Base):
    __tablename__ = "mappings"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    run_id: Mapped[int] = mapped_column(ForeignKey("extraction_runs.id"), nullable=False)
    source_label: Mapped[str] = mapped_column(String(255), nullable=False)
    source_value: Mapped[str] = mapped_column(Text, nullable=False)
    target_row: Mapped[int] = mapped_column(Integer, nullable=False)
    target_column: Mapped[int] = mapped_column(Integer, nullable=False)
    confidence: Mapped[float] = mapped_column(Float, default=0.0)
    region_json: Mapped[str] = mapped_column(Text, default="")

    run: Mapped[ExtractionRunRecord] = relationship(back_populates="mappings")


class ExtractedValueRecord(Base):
    __tablename__ = "extracted_values"

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    run_id: Mapped[int] = mapped_column(ForeignKey("extraction_runs.id"), nullable=False)
    row: Mapped[int] = mapped_column(Integer, nullable=False)
    column: Mapped[int] = mapped_column(Integer, nullable=False)
    field_name: Mapped[str] = mapped_column(String(255), default="")
    value: Mapped[str] = mapped_column(Text, default="")
    confidence: Mapped[float] = mapped_column(Float, default=0.0)

    run: Mapped[ExtractionRunRecord] = relationship(back_populates="values")
