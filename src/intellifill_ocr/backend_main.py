from __future__ import annotations

import sys

from intellifill_ocr.ipc.server import main as ipc_main


def main() -> int:
    return ipc_main(sys.argv[1:])


if __name__ == "__main__":
    raise SystemExit(main())
