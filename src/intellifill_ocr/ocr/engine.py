from __future__ import annotations

import logging
from pathlib import Path

import cv2
import pytesseract
from PIL import Image
from pytesseract import Output, TesseractNotFoundError

from intellifill_ocr.models.document import OCRBox
from intellifill_ocr.ocr.preprocess import denoise_and_threshold, deskew, load_image
from intellifill_ocr.utils.exceptions import OCRUnavailableError

LOGGER = logging.getLogger(__name__)


class OCREngine:
    def __init__(self, tesseract_cmd: str | None = None, language: str = "eng") -> None:
        self.language = language
        if tesseract_cmd:
            pytesseract.pytesseract.tesseract_cmd = tesseract_cmd

    def image_to_text(self, path: Path, region: tuple[int, int, int, int] | None = None) -> tuple[str, list[OCRBox]]:
        try:
            image = load_image(str(path))
            if region:
                x, y, width, height = region
                image = image[max(y, 0) : max(y + height, 0), max(x, 0) : max(x + width, 0)]
            processed = denoise_and_threshold(deskew(image))
            pil_image = Image.fromarray(processed)
            data = pytesseract.image_to_data(pil_image, lang=self.language, output_type=Output.DICT)
        except TesseractNotFoundError as exc:
            raise OCRUnavailableError("Tesseract OCR was not found. Install it locally or set TESSERACT_CMD.") from exc

        boxes: list[OCRBox] = []
        words: list[str] = []
        for i, text in enumerate(data.get("text", [])):
            value = str(text).strip()
            if not value:
                continue
            try:
                confidence = float(data["conf"][i])
            except (TypeError, ValueError):
                confidence = 0.0
            offset_x, offset_y = region[:2] if region else (0, 0)
            boxes.append(
                OCRBox(
                    text=value,
                    confidence=max(confidence, 0.0),
                    x=int(data["left"][i]) + offset_x,
                    y=int(data["top"][i]) + offset_y,
                    width=int(data["width"][i]),
                    height=int(data["height"][i]),
                )
            )
            words.append(value)
        return " ".join(words), boxes

    def boxes_to_lines(self, boxes: list[OCRBox], y_tolerance: int = 12) -> list[str]:
        lines: list[list[OCRBox]] = []
        for box in sorted(boxes, key=lambda item: (item.y, item.x)):
            for line in lines:
                if abs(line[0].y - box.y) <= y_tolerance:
                    line.append(box)
                    break
            else:
                lines.append([box])
        return [" ".join(box.text for box in sorted(line, key=lambda item: item.x)) for line in lines]


def save_debug_overlay(image_path: Path, boxes: list[OCRBox], output_path: Path) -> None:
    image = load_image(str(image_path))
    for box in boxes:
        color = (0, 180, 0) if box.confidence >= 70 else (0, 160, 255)
        cv2.rectangle(image, (box.x, box.y), (box.x + box.width, box.y + box.height), color, 2)
    cv2.imwrite(str(output_path), image)
    LOGGER.info("Wrote OCR overlay to %s", output_path)
