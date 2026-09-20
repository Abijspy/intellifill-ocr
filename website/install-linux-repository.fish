#!/usr/bin/env fish

# Website copy of the Fish installer. The packaged canonical copy is kept in scripts/.
set -l assume_yes false
if contains -- --yes $argv; or contains -- -y $argv
    set assume_yes true
end

function ui_title
    echo
    set_color --bold cyan
    echo "IntelliFill OCR · Fish installer"
    set_color normal
end

function ui_step
    set_color cyan
    printf "› "
    set_color normal
    echo $argv
end

function ui_done
    set_color green
    printf "✓ "
    set_color normal
    echo $argv
end

ui_title
ui_step "Preparing the official repository installer…"
set -l script_url "https://abishekprabakaran.com/intellifill-ocr/install-linux-repository.sh"
set -l temporary_script (mktemp)
curl -fsSL "$script_url" -o "$temporary_script"; or begin
    set_color red
    echo "Could not download the installer. Check your internet connection and try again." >&2
    set_color normal
    exit 1
end
ui_done "Installer downloaded to a temporary location"

if status is-interactive; and test "$assume_yes" != true
    read --nchars 1 --prompt-str "Configure the IntelliFill OCR repository now? [Y/n] " response
    echo
    if test -n "$response"; and not string match -rq '^[Yy]$' -- "$response"
        echo "No changes were made."
        exit 0
    end
end

ui_step "Starting the guided repository setup…"
if test (id -u) -ne 0
    exec sudo bash "$temporary_script" --yes
end
exec bash "$temporary_script" --yes
