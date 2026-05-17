from __future__ import annotations

from io import BytesIO

from PySide6.QtCore import QRect, Qt
from PySide6.QtGui import QColor, QFont, QPainter, QPixmap
from PIL import Image, ImageDraw


CODE39 = {
    "0": "nnnwwnwnn",
    "1": "wnnwnnnnw",
    "2": "nnwwnnnnw",
    "3": "wnwwnnnnn",
    "4": "nnnwwnnnw",
    "5": "wnnwwnnnn",
    "6": "nnwwwnnnn",
    "7": "nnnwnnwnw",
    "8": "wnnwnnwnn",
    "9": "nnwwnnwnn",
    "A": "wnnnnwnnw",
    "B": "nnwnnwnnw",
    "C": "wnwnnwnnn",
    "D": "nnnnwwnnw",
    "E": "wnnnwwnnn",
    "F": "nnwnwwnnn",
    "G": "nnnnnwwnw",
    "H": "wnnnnwwnn",
    "I": "nnwnnwwnn",
    "J": "nnnnwwwnn",
    "K": "wnnnnnnww",
    "L": "nnwnnnnww",
    "M": "wnwnnnnwn",
    "N": "nnnnwnnww",
    "O": "wnnnwnnwn",
    "P": "nnwnwnnwn",
    "Q": "nnnnnnwww",
    "R": "wnnnnnwwn",
    "S": "nnwnnnwwn",
    "T": "nnnnwnwwn",
    "U": "wwnnnnnnw",
    "V": "nwwnnnnnw",
    "W": "wwwnnnnnn",
    "X": "nwnnwnnnw",
    "Y": "wwnnwnnnn",
    "Z": "nwwnwnnnn",
    "-": "nwnnnnwnw",
    ".": "wwnnnnwnn",
    " ": "nwwnnnwnn",
    "$": "nwnwnwnnn",
    "/": "nwnwnnnwn",
    "+": "nwnnnwnwn",
    "%": "nnnwnwnwn",
    "*": "nwnnwnwnn",
}


def normalize_code39(value: str) -> str:
    return "".join(ch for ch in value.upper() if ch in CODE39 and ch != "*")


def code39_width(value: str, narrow: int = 2) -> int:
    encoded = f"*{normalize_code39(value)}*"
    wide = narrow * 3
    width = 0
    for char in encoded:
        for marker in CODE39[char]:
            width += wide if marker == "w" else narrow
        width += narrow
    return width


def draw_code39(
    painter: QPainter,
    value: str,
    x: int,
    y: int,
    height: int,
    narrow: int = 2,
    show_text: bool = True,
) -> int:
    encoded = f"*{normalize_code39(value)}*"
    wide = narrow * 3
    cursor = x
    painter.save()
    painter.setPen(Qt.PenStyle.NoPen)
    painter.setBrush(QColor("#111827"))
    for char in encoded:
        pattern = CODE39[char]
        for index, marker in enumerate(pattern):
            width = wide if marker == "w" else narrow
            if index % 2 == 0:
                painter.drawRect(cursor, y, width, height)
            cursor += width
        cursor += narrow
    if show_text:
        painter.setPen(QColor("#111827"))
        painter.setFont(QFont("Segoe UI", 8))
        painter.drawText(QRect(x, y + height + 2, max(cursor - x, 180), 18), Qt.AlignmentFlag.AlignLeft, value)
    painter.restore()
    return cursor - x


def barcode_pixmap(value: str, width: int = 320, height: int = 68) -> QPixmap:
    pixmap = QPixmap(width, height)
    pixmap.fill(QColor("#ffffff"))
    painter = QPainter(pixmap)
    try:
        barcode_width = code39_width(value, narrow=1)
        x = max(8, int((width - barcode_width) / 2))
        draw_code39(painter, value, x, 8, 36, narrow=1, show_text=True)
    finally:
        painter.end()
    return pixmap


def barcode_png_bytes(value: str, narrow: int = 1, bar_height: int = 34, show_text: bool = True) -> BytesIO:
    encoded = f"*{normalize_code39(value)}*"
    quiet_zone = 12
    text_height = 18 if show_text else 0
    width = max(240, code39_width(value, narrow=narrow) + quiet_zone * 2)
    height = bar_height + text_height + quiet_zone * 2
    wide = narrow * 3
    image = Image.new("RGB", (width, height), "white")
    draw = ImageDraw.Draw(image)
    cursor = quiet_zone
    top = quiet_zone

    for char in encoded:
        pattern = CODE39[char]
        for index, marker in enumerate(pattern):
            bar_width = wide if marker == "w" else narrow
            if index % 2 == 0:
                draw.rectangle((cursor, top, cursor + bar_width - 1, top + bar_height), fill="black")
            cursor += bar_width
        cursor += narrow

    if show_text:
        draw.text((quiet_zone, top + bar_height + 4), value, fill="black")

    output = BytesIO()
    image.save(output, format="PNG")
    output.seek(0)
    return output
