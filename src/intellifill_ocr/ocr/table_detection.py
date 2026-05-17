from __future__ import annotations

import cv2
import numpy as np

from intellifill_ocr.ocr.preprocess import denoise_and_threshold, load_image


def detect_table_cells(path: str) -> list[tuple[int, int, int, int]]:
    image = load_image(path)
    binary = 255 - denoise_and_threshold(image)
    horizontal_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (40, 1))
    vertical_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (1, 30))
    horizontal = cv2.morphologyEx(binary, cv2.MORPH_OPEN, horizontal_kernel, iterations=2)
    vertical = cv2.morphologyEx(binary, cv2.MORPH_OPEN, vertical_kernel, iterations=2)
    grid = cv2.add(horizontal, vertical)
    contours, _ = cv2.findContours(grid, cv2.RETR_TREE, cv2.CHAIN_APPROX_SIMPLE)
    cells: list[tuple[int, int, int, int]] = []
    for contour in contours:
        x, y, w, h = cv2.boundingRect(contour)
        if w >= 20 and h >= 12:
            cells.append((x, y, w, h))
    return sorted(cells, key=lambda rect: (rect[1], rect[0]))


def cluster_rows_columns(cells: list[tuple[int, int, int, int]], tolerance: int = 10) -> list[list[tuple[int, int, int, int]]]:
    rows: list[list[tuple[int, int, int, int]]] = []
    for cell in cells:
        for row in rows:
            if abs(row[0][1] - cell[1]) <= tolerance:
                row.append(cell)
                break
        else:
            rows.append([cell])
    return [sorted(row, key=lambda rect: rect[0]) for row in rows]
