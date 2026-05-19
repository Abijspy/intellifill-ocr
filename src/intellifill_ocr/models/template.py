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
    table_index: int = 0
    source_page: int = 0
    bbox: tuple[float, float, float, float] | None = None


@dataclass
class TemplateTable:
    name: str
    cells: list[list[TemplateCell]] = field(default_factory=list)
    table_index: int = 0
    display_name: str = ""
    document_tables: list["TemplateTable"] = field(default_factory=list, repr=False, compare=False)

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
            self.cells[row].append(
                TemplateCell(row=row, column=len(self.cells[row]), table_index=self.table_index)
            )
        self.cells[row][column].value = value

    def all_tables(self) -> list["TemplateTable"]:
        return self.document_tables or [self]

    @property
    def table_count(self) -> int:
        return len(self.all_tables())

    @property
    def label(self) -> str:
        return self.display_name or f"Table {self.table_index + 1}"

    def field_candidates(self) -> list[tuple[int, int, str]]:
        candidates: list[tuple[int, int, str]] = []
        for row_index, row in enumerate(self.cells):
            for column_index, cell in enumerate(row):
                if cell.value.strip():
                    candidates.append((row_index, column_index, cell.value.strip()))
        return candidates
