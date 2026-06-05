from __future__ import annotations

from io import BytesIO

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


def barcode_png_bytes(
    value: str,
    narrow: int = 1,
    bar_height: int = 34,
    show_text: bool = True,
    quiet_zone: int = 12,
) -> BytesIO:
    encoded = f"*{normalize_code39(value)}*"
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
                draw.rectangle((cursor, top, cursor + bar_width - 1, top + bar_height - 1), fill="black")
            cursor += bar_width
        cursor += narrow

    if show_text:
        draw.text((quiet_zone, top + bar_height + 4), value, fill="black")

    output = BytesIO()
    image.save(output, format="PNG")
    output.seek(0)
    return output
