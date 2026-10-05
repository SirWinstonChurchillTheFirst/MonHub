#!/bin/sh
# MonHub for Linux – run once after unpacking the archive (and again after moving the folder).
# It unpacks the emulators, so they start on every distribution (also where AppImages need extra packages), and
# puts MonHub into the applications menu. Nothing is written outside this folder except that menu entry.
set -e
cd "$(dirname "$(readlink -f "$0")")"
HERE="$(pwd)"

chmod +x System/App/MonHub System/App/createdump 2>/dev/null || true

for emu in melonDS mGBA Azahar; do
    dir="System/Emulatoren/$emu"
    img="$(ls "$dir"/*.AppImage 2>/dev/null | head -n 1)"
    [ -n "$img" ] || continue
    echo "Unpacking $emu ..."
    chmod +x "$img"
    if ( cd "$dir" && rm -rf squashfs-root app && "./$(basename "$img")" --appimage-extract >/dev/null 2>&1 && mv squashfs-root app ); then
        rm -f "$img"
    else
        # stays as it is: MonHub then starts the AppImage itself
        ( cd "$dir" && rm -rf squashfs-root )
        echo "  could not unpack $emu – MonHub will start it as an AppImage."
    fi
done

APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$APPS"
cat > "$APPS/monhub.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=MonHub
Comment=Your game hub
Comment[de]=Dein Spielzentrum
Exec="$HERE/System/App/MonHub"
Path=$HERE/System/App
Icon=$HERE/System/App/monhub.png
Terminal=false
Categories=Game;
StartupWMClass=MonHub
EOF
chmod +x "$APPS/monhub.desktop"
update-desktop-database "$APPS" >/dev/null 2>&1 || true

echo
echo "Done. MonHub is in your applications menu now."
echo "You can also start it directly: $HERE/System/App/MonHub"
