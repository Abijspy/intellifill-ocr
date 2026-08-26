#!/usr/bin/env python3
"""Generate AppStream and package changelog metadata from the in-app changelog."""

from __future__ import annotations

import argparse
import datetime as dt
import email.utils
import html
import re
from pathlib import Path


def release_items(source: str, version: str) -> list[str]:
    match = re.search(
        rf"^\s*Version\s+{re.escape(version)}\s*$\n(?P<body>.*?)(?=^\s*Version\s+\d|^\s*\"\"\";)",
        source,
        flags=re.MULTILINE | re.DOTALL,
    )
    if not match:
        raise SystemExit(f"Version {version} was not found in the in-app changelog.")
    items = [line.strip()[2:].strip() for line in match.group("body").splitlines() if line.strip().startswith("- ")]
    if not items:
        raise SystemExit(f"Version {version} has no changelog items.")
    return items


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("version")
    parser.add_argument("changelog_source", type=Path)
    parser.add_argument("appstream_template", type=Path)
    parser.add_argument("changelog_template", type=Path)
    parser.add_argument("appstream_output", type=Path)
    parser.add_argument("changelog_output", type=Path)
    args = parser.parse_args()

    if not re.fullmatch(r"\d+\.\d+\.\d+(?:\.\d+)?", args.version):
        raise SystemExit(f"Invalid release version: {args.version}")

    items = release_items(args.changelog_source.read_text(encoding="utf-8"), args.version)
    now = dt.datetime.now(dt.timezone.utc)
    appstream_items = "\n".join(f"          <li>{html.escape(item)}</li>" for item in items)
    debian_items = "\n".join(f"  * {item}" for item in items)

    appstream = args.appstream_template.read_text(encoding="utf-8")
    appstream = appstream.replace("@VERSION@", args.version)
    appstream = appstream.replace("@DATE@", now.date().isoformat())
    appstream = appstream.replace("@RELEASE_ITEMS@", appstream_items)

    changelog = args.changelog_template.read_text(encoding="utf-8")
    changelog = changelog.replace("@VERSION@", args.version)
    changelog = changelog.replace("@CHANGELOG_ITEMS@", debian_items)
    changelog = changelog.replace("@DEB_DATE@", email.utils.format_datetime(now))

    args.appstream_output.parent.mkdir(parents=True, exist_ok=True)
    args.changelog_output.parent.mkdir(parents=True, exist_ok=True)
    args.appstream_output.write_text(appstream, encoding="utf-8")
    args.changelog_output.write_text(changelog, encoding="utf-8")


if __name__ == "__main__":
    main()
