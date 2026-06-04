from __future__ import annotations

import argparse
import json
import logging
import sys
from typing import Any, TextIO

from intellifill_ocr.ipc.backend import IpcBackend
from intellifill_ocr.utils.exceptions import IntelliFillError


LOGGER = logging.getLogger(__name__)


def response_ok(request_id: object, result: dict[str, Any]) -> dict[str, Any]:
    return {"id": request_id, "ok": True, "result": result}


def response_error(request_id: object, exc: Exception) -> dict[str, Any]:
    return {
        "id": request_id,
        "ok": False,
        "error": {
            "type": type(exc).__name__,
            "message": str(exc),
        },
    }


def serve(input_stream: TextIO = sys.stdin, output_stream: TextIO = sys.stdout) -> int:
    backend = IpcBackend()
    for line in input_stream:
        line = line.strip()
        if not line:
            continue
        request_id: object = None
        try:
            request = json.loads(line)
            if not isinstance(request, dict):
                raise ValueError("IPC request must be a JSON object.")
            request_id = request.get("id")
            command = str(request.get("command") or "")
            params = request.get("params") or {}
            if not isinstance(params, dict):
                raise ValueError("IPC params must be a JSON object.")
            payload = response_ok(request_id, backend.handle(command, params))
        except (IntelliFillError, OSError, ValueError, TypeError, json.JSONDecodeError) as exc:
            LOGGER.exception("IPC command failed")
            payload = response_error(request_id, exc)
        except Exception as exc:  # noqa: BLE001
            LOGGER.exception("Unexpected IPC backend failure")
            payload = response_error(request_id, exc)
        output_stream.write(json.dumps(payload, ensure_ascii=False) + "\n")
        output_stream.flush()
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Run the IntelliFill OCR JSON IPC backend.")
    parser.add_argument("--stdio", action="store_true", help="Read JSON requests from stdin and write JSON responses to stdout.")
    parser.parse_args(argv)
    return serve()


if __name__ == "__main__":
    raise SystemExit(main())
