#!/usr/bin/env fish

# Website copy of the Fish installer. The packaged canonical copy is kept in scripts/.
set -l script_url "https://abishekprabakaran.com/intellifill-ocr/install-linux-repository.sh"
set -l temporary_script (mktemp)
curl -fsSL "$script_url" -o "$temporary_script"; or exit 1
if test (id -u) -ne 0
    exec sudo bash "$temporary_script" $argv
end
exec bash "$temporary_script" $argv
