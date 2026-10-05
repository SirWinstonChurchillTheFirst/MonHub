#!/bin/sh
# Takes MonHub out of the applications menu. Your games and saves are in this folder – it is NOT deleted.
# To remove MonHub completely, delete the folder afterwards (copy your ROMs and "Spielstände" out first).
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
rm -f "$APPS/monhub.desktop"
update-desktop-database "$APPS" >/dev/null 2>&1 || true
echo "MonHub was removed from the applications menu."
echo "This folder with your ROMs and saves is still there: $(dirname "$(readlink -f "$0")")"
