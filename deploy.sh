#!/bin/sh
# Builds TG3ModLoader and optionally copies it into the Steam Workshop folder of The Guild 3.
# Usage: sh deploy.sh [<steamapps>/workshop/content/311260/TG3ModLoader]
#        (the target can also be set with the environment variable TG3_MODLOADER_DIR)
set -e
ROOT="$(cd "$(dirname "$0")" && pwd)"
TARGET="${1:-$TG3_MODLOADER_DIR}"
cd "$ROOT/src"
dotnet build -c Release -o "$ROOT/build" -nologo -v q
if [ -z "$TARGET" ]; then
  echo "Built to $ROOT/build (no target folder given, nothing copied)."
  exit 0
fi
mkdir -p "$TARGET"
cp "$ROOT/build/TG3ModLoader.exe" "$ROOT/build/TG3ModLoader.exe.config" "$TARGET/"
cp "$ROOT/README.txt" "$TARGET/TG3ModLoader-README.txt"
cp "$ROOT/LICENSE" "$TARGET/TG3ModLoader-LICENSE.txt"
rm -f "$TARGET/README.txt"   # renamed in 1.3.0 (unique names: Workshop items share one folder in the launcher)
echo "Deployed to $TARGET"
