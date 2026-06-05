# -*- mode: python ; coding: utf-8 -*-

from PyInstaller.utils.hooks import collect_submodules
from pathlib import Path

SPEC_DIR = Path(SPECPATH).resolve()
ROOT = SPEC_DIR.parent
SRC = ROOT / "src"
DEMO = ROOT / "demo"
ASSETS = ROOT / "assets"

hiddenimports = []
hiddenimports += collect_submodules("pytesseract")
hiddenimports += collect_submodules("sqlalchemy")
hiddenimports += collect_submodules("intellifill_ocr.ipc")

a = Analysis(
    [str(SRC / "intellifill_ocr" / "backend_main.py")],
    pathex=[str(SRC)],
    binaries=[],
    datas=[(str(DEMO), "demo"), (str(ASSETS), "assets")],
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=["PySide6"],
    noarchive=False,
)
pyz = PYZ(a.pure)
exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="IntelliFillOCRBackend",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=str(ASSETS / "app.ico"),
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name="IntelliFillOCRBackend",
)
