#!/bin/bash
set -e
cd "$(dirname "$0")"

dotnet build FMAI.sln -c Debug

TOOLS_DIR="$(pwd)/BizHawk/ExternalTools"
CONFIG_FILE="./BizHawk/config.ini"

# Trust every tool DLL so BizHawk doesn't prompt
if [ -f "$CONFIG_FILE" ]; then
    for name in FMAI HexViewer; do
        dll="$TOOLS_DIR/$name.dll"
        [ -f "$dll" ] || { echo "Error: $dll not found"; exit 1; }
        hash="SHA512:$(sha512sum "$dll" | awk '{print toupper($1)}')"
        tmp=$(mktemp)
        jq --arg path "$dll" --arg hash "$hash" \
           '.TrustedExtTools[$path] = $hash' "$CONFIG_FILE" > "$tmp" && mv "$tmp" "$CONFIG_FILE"
    done
fi

exec ./BizHawk/EmuHawkMono.sh --open-ext-tool-dll=FMAI.dll ~/repos/FMAI/front_mission_japan.zip "$@"