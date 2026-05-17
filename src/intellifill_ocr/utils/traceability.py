from __future__ import annotations

from datetime import datetime
from uuid import uuid4


def new_traceability_code() -> str:
    timestamp = datetime.now().strftime("%Y%m%d%H%M%S")
    suffix = uuid4().hex[:6].upper()
    return f"IF-{timestamp}-{suffix}"
