from __future__ import annotations

import logging
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np

from intellifill_ocr.models.document import ParsedDocument
from intellifill_ocr.utils.paths import app_data_dir

LOGGER = logging.getLogger(__name__)


@dataclass(frozen=True)
class DetectedMark:
    kind: str
    source_path: Path
    page: int
    x: int
    y: int
    width: int
    height: int
    confidence: float
    reason: str


class SignatureStampDetector:
    """Heuristic offline signature and stamp detector for office documents."""

    IMAGE_TYPES = {".png", ".jpg", ".jpeg"}

    def detect(self, documents: list[ParsedDocument], max_pdf_pages: int = 3) -> list[DetectedMark]:
        marks: list[DetectedMark] = []
        for document in documents:
            suffix = document.path.suffix.lower()
            try:
                if suffix in self.IMAGE_TYPES:
                    marks.extend(self._detect_image(document.path, document.path, 0, document.text))
                elif suffix == ".pdf":
                    for page_index, image_path in enumerate(self._render_pdf_pages(document.path, max_pdf_pages)):
                        marks.extend(self._detect_image(image_path, document.path, page_index, document.text))
                else:
                    marks.extend(self._text_only_marks(document))
            except Exception:
                LOGGER.exception("Signature/stamp detection failed for %s", document.path)
        return self._dedupe(marks)

    def _detect_image(self, image_path: Path, source_path: Path, page: int, text: str) -> list[DetectedMark]:
        image = cv2.imread(str(image_path))
        if image is None:
            return []
        height, width = image.shape[:2]
        marks = self._detect_signature_like_ink(image, source_path, page)
        marks.extend(self._detect_stamp_like_color(image, source_path, page))
        if self._has_signature_keywords(text) and not marks:
            marks.append(
                DetectedMark(
                    kind="Signature area",
                    source_path=source_path,
                    page=page,
                    x=int(width * 0.55),
                    y=int(height * 0.72),
                    width=int(width * 0.35),
                    height=int(height * 0.16),
                    confidence=45.0,
                    reason="signature/approval keyword found, no ink mark confirmed",
                )
            )
        return marks

    def _detect_signature_like_ink(self, image: np.ndarray, source_path: Path, page: int) -> list[DetectedMark]:
        height, width = image.shape[:2]
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (3, 3), 0)
        threshold = cv2.adaptiveThreshold(
            gray,
            255,
            cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
            cv2.THRESH_BINARY_INV,
            35,
            12,
        )
        lower_half = np.zeros_like(threshold)
        lower_half[int(height * 0.48) :, :] = threshold[int(height * 0.48) :, :]
        kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (13, 3))
        merged = cv2.dilate(lower_half, kernel, iterations=2)
        contours, _hierarchy = cv2.findContours(merged, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        marks: list[DetectedMark] = []
        page_area = float(width * height)
        for contour in contours:
            x, y, box_width, box_height = cv2.boundingRect(contour)
            area = box_width * box_height
            if area < page_area * 0.0005 or area > page_area * 0.08:
                continue
            aspect = box_width / max(box_height, 1)
            if aspect < 2.0 or box_width < max(70, width * 0.08) or box_height > height * 0.20:
                continue
            ink_pixels = cv2.countNonZero(threshold[y : y + box_height, x : x + box_width])
            fill_ratio = ink_pixels / max(area, 1)
            if fill_ratio > 0.42:
                continue
            confidence = min(92.0, 52.0 + aspect * 4.0 + (1.0 - fill_ratio) * 22.0)
            marks.append(
                DetectedMark(
                    kind="Signature",
                    source_path=source_path,
                    page=page,
                    x=x,
                    y=y,
                    width=box_width,
                    height=box_height,
                    confidence=round(confidence, 1),
                    reason="wide low-density ink in lower document area",
                )
            )
        return marks

    def _detect_stamp_like_color(self, image: np.ndarray, source_path: Path, page: int) -> list[DetectedMark]:
        hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
        color_masks = [
            cv2.inRange(hsv, np.array([0, 55, 45]), np.array([12, 255, 255])),
            cv2.inRange(hsv, np.array([165, 55, 45]), np.array([179, 255, 255])),
            cv2.inRange(hsv, np.array([90, 40, 45]), np.array([135, 255, 255])),
        ]
        mask = color_masks[0]
        for color_mask in color_masks[1:]:
            mask = cv2.bitwise_or(mask, color_mask)
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7)))
        contours, _hierarchy = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        height, width = image.shape[:2]
        page_area = float(width * height)
        marks: list[DetectedMark] = []
        for contour in contours:
            x, y, box_width, box_height = cv2.boundingRect(contour)
            area = box_width * box_height
            if area < page_area * 0.0008 or area > page_area * 0.10:
                continue
            ratio = box_width / max(box_height, 1)
            if ratio < 0.45 or ratio > 2.4:
                continue
            contour_area = cv2.contourArea(contour)
            fill_ratio = contour_area / max(area, 1)
            confidence = min(94.0, 48.0 + fill_ratio * 42.0)
            marks.append(
                DetectedMark(
                    kind="Stamp",
                    source_path=source_path,
                    page=page,
                    x=x,
                    y=y,
                    width=box_width,
                    height=box_height,
                    confidence=round(confidence, 1),
                    reason="red/blue stamp-like colored mark",
                )
            )
        return marks

    def _render_pdf_pages(self, path: Path, max_pages: int) -> list[Path]:
        import pypdfium2 as pdfium

        output_dir = app_data_dir() / "detection_previews"
        output_dir.mkdir(parents=True, exist_ok=True)
        pdf = pdfium.PdfDocument(str(path))
        paths: list[Path] = []
        for page_index in range(min(len(pdf), max_pages)):
            image_path = output_dir / f"{path.stem}_{int(path.stat().st_mtime)}_page_{page_index + 1}.png"
            if not image_path.exists():
                bitmap = pdf[page_index].render(scale=1.7).to_pil()
                bitmap.save(image_path)
            paths.append(image_path)
        return paths

    def _text_only_marks(self, document: ParsedDocument) -> list[DetectedMark]:
        if not self._has_signature_keywords(document.text):
            return []
        return [
            DetectedMark(
                kind="Signature area",
                source_path=document.path,
                page=0,
                x=0,
                y=0,
                width=0,
                height=0,
                confidence=38.0,
                reason="signature/approval keyword found in parsed text",
            )
        ]

    def _has_signature_keywords(self, text: str) -> bool:
        lowered = text.lower()
        keywords = ("signature", "signed", "stamp", "approved by", "rejected by", "authorized signatory")
        return any(keyword in lowered for keyword in keywords)

    def _dedupe(self, marks: list[DetectedMark]) -> list[DetectedMark]:
        unique: list[DetectedMark] = []
        for mark in sorted(marks, key=lambda item: item.confidence, reverse=True):
            if any(self._overlaps(mark, existing) for existing in unique):
                continue
            unique.append(mark)
        return unique

    def _overlaps(self, left: DetectedMark, right: DetectedMark) -> bool:
        if left.source_path != right.source_path or left.page != right.page:
            return False
        if min(left.width, left.height, right.width, right.height) <= 0:
            return False
        lx2, ly2 = left.x + left.width, left.y + left.height
        rx2, ry2 = right.x + right.width, right.y + right.height
        overlap_width = max(0, min(lx2, rx2) - max(left.x, right.x))
        overlap_height = max(0, min(ly2, ry2) - max(left.y, right.y))
        overlap_area = overlap_width * overlap_height
        smaller_area = min(left.width * left.height, right.width * right.height)
        return overlap_area / max(smaller_area, 1) > 0.45
