$ErrorActionPreference = "Stop"

if (-not (Test-Path ".venv")) {
    python -m venv .venv
}

.\.venv\Scripts\python.exe -m pip install --upgrade pip
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\pyinstaller.exe .\pyinstaller\IntelliFillOCR.spec --clean --noconfirm

Write-Host "Built dist\IntelliFillOCR\IntelliFillOCR.exe"
