from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class TemplateCell:
    row: int
    column: int
    value: str = ""
    is_placeholder: bool = False
    row_span: int = 1
    column_span: int = 1
    source_page: int = 0
    bbox: tuple[float, float, float, float] | None = None


@dataclass
class TemplateTable:
    name: str
    cells: list[list[TemplateCell]] = field(default_factory=list)

    @property
    def row_count(self) -> int:
        return len(self.cells)

    @property
    def column_count(self) -> int:
        return max((len(row) for row in self.cells), default=0)

    def value_at(self, row: int, column: int) -> str:
        try:
            return self.cells[row][column].value
        except IndexError:
            return ""

    def set_value(self, row: int, column: int, value: str) -> None:
        while len(self.cells) <= row:
            self.cells.append([])
        while len(self.cells[row]) <= column:
            self.cells[row].append(TemplateCell(row=row, column=len(self.cells[row])))
        self.cells[row][column].value = value

    def field_candidates(self) -> list[tuple[int, int, str]]:
        candidates: list[tuple[int, int, str]] = []
        for row_index, row in enumerate(self.cells):
            for column_index, cell in enumerate(row):
                if cell.value.strip():
                    candidates.append((row_index, column_index, cell.value.strip()))
        return candidates
