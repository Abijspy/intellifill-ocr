from __future__ import annotations

from datetime import datetime
from uuid import uuid4


def new_traceability_code() -> str:
    timestamp = datetime.now().strftime("%y%m%d%H%M")
    suffix = uuid4().hex[:5].upper()
    return f"IF{timestamp}{suffix}"
