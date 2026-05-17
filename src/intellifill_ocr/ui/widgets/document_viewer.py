from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import QRectF, Qt, Signal
from PySide6.QtGui import QBrush, QColor, QPen, QPixmap
from PySide6.QtWidgets import QGraphicsPixmapItem, QGraphicsRectItem, QGraphicsScene, QGraphicsView

from intellifill_ocr.models.document import OCRBox


class DocumentViewer(QGraphicsView):
    region_selected = Signal(int, int, int, int)

    def __init__(self) -> None:
        super().__init__()
        self.scene = QGraphicsScene(self)
        self.setScene(self.scene)
        self.pixmap_item: QGraphicsPixmapItem | None = None
        self.selection_item: QGraphicsRectItem | None = None
        self.origin = None
        self.zoom_factor = 1.0
        self.setDragMode(QGraphicsView.DragMode.NoDrag)
        self.setRenderHints(self.renderHints())

    def load_image(self, path: Path) -> None:
        self.scene.clear()
        pixmap = QPixmap(str(path))
        self.pixmap_item = self.scene.addPixmap(pixmap)
        self.setSceneRect(QRectF(pixmap.rect()))
        self.fitInView(self.sceneRect(), Qt.AspectRatioMode.KeepAspectRatio)

    def show_boxes(self, boxes: list[OCRBox]) -> None:
        for box in boxes:
            color = QColor("#4ade80") if box.confidence >= 70 else QColor("#f59e0b")
            item = self.scene.addRect(box.x, box.y, box.width, box.height, QPen(color, 2), QBrush(Qt.BrushStyle.NoBrush))
            item.setToolTip(f"{box.text} ({box.confidence:.0f}%)")

    def wheelEvent(self, event) -> None:  # noqa: N802
        factor = 1.15 if event.angleDelta().y() > 0 else 0.87
        self.zoom_factor *= factor
        self.scale(factor, factor)

    def mousePressEvent(self, event) -> None:  # noqa: N802
        if event.button() == Qt.MouseButton.LeftButton:
            self.origin = self.mapToScene(event.position().toPoint())
            if self.selection_item:
                self.scene.removeItem(self.selection_item)
            self.selection_item = self.scene.addRect(QRectF(self.origin, self.origin), QPen(QColor("#60a5fa"), 2))
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event) -> None:  # noqa: N802
        if self.origin and self.selection_item:
            current = self.mapToScene(event.position().toPoint())
            self.selection_item.setRect(QRectF(self.origin, current).normalized())
        super().mouseMoveEvent(event)

    def mouseReleaseEvent(self, event) -> None:  # noqa: N802
        if self.origin and self.selection_item:
            rect = self.selection_item.rect().toRect()
            if rect.width() > 5 and rect.height() > 5:
                self.region_selected.emit(rect.x(), rect.y(), rect.width(), rect.height())
        self.origin = None
        super().mouseReleaseEvent(event)
