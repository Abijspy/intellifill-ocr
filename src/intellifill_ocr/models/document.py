from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class OCRBox:
    text: str
    confidence: float
    x: int
    y: int
    width: int
    height: int
    page: int = 0

    @property
    def rect(self) -> tuple[int, int, int, int]:
        return self.x, self.y, self.width, self.height


@dataclass
class ParsedDocument:
    path: Path
    text: str = ""
    tables: list[list[list[str]]] = field(default_factory=list)
    ocr_boxes: list[OCRBox] = field(default_factory=list)
    metadata: dict[str, str] = field(default_factory=dict)


@dataclass
class RegionSelection:
    source_path: Path
    page: int
    x: int
    y: int
    width: int
    height: int


@dataclass
class ExtractedField:
    label: str
    value: str
    confidence: float
    source_path: Path | None = None
    source_region: RegionSelection | None = None
