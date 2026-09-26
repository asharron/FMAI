#!/bin/bash
set -e
cd "$(dirname "$0")"

# build and find dll
dotnet build -c Debug
BUILT_DLL=$(find bin/Debug -name "FMAI.dll" | head -n 1)
if [ -z "$BUILT_DLL" ]; then
    echo "Error: Could not find compiled FMAI.dll in bin/Debug"
    exit 1
fi

# Copy dll to external tools for bizhawk
mkdir -p ./BizHawk/ExternalTools
cp "$BUILT_DLL" ./BizHawk/ExternalTools/
TARGET_DLL_PATH="$(pwd)/BizHawk/ExternalTools/FMAI.dll"

# Calculate sha hash and update config.ini to bypass allow trusted tool prompt on bizhawk run
RAW_HASH=$(sha512sum "$TARGET_DLL_PATH" | awk '{print toupper($1)}')
FULL_HASH="SHA512:$RAW_HASH"

CONFIG_FILE="./BizHawk/config.ini"
if [ -f "$CONFIG_FILE" ]; then
    tmp=$(mktemp)
    jq --arg path "$TARGET_DLL_PATH" --arg hash "$FULL_HASH" '
      .TrustedExtTools[$path] = $hash
    ' "$CONFIG_FILE" > "$tmp" && mv "$tmp" "$CONFIG_FILE"
fi

# Run bizhawk with the dll
exec ./BizHawk/EmuHawkMono.sh --open-ext-tool-dll=FMAI.dll "$@"