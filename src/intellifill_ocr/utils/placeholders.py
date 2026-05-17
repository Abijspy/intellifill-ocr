from __future__ import annotations


def is_placeholder_text(value: object | None) -> bool:
    """Return True for blank cells and common visible placeholder tokens."""
    if value is None:
        return True

    text = str(value).strip()
    if not text:
        return True

    compact = "".join(text.split())
    if len(compact) >= 3 and set(compact) <= {"_"}:
        return True

    bracket_pairs = (("{", "}"), ("{{", "}}"), ("[", "]"), ("<", ">"))
    return any(text.startswith(start) and text.endswith(end) for start, end in bracket_pairs)
