#!/usr/bin/env bash
# Build and launch the game. Usage: tools/run.sh [--skip-tests]   (GODOT=/path/to/godot to override)
set -euo pipefail
cd "$(dirname "$0")/.."

need() { command -v "$1" >/dev/null 2>&1 || { echo "error: '$1' not found on PATH. $2" >&2; exit 1; }; }
need dotnet "Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
GODOT="${GODOT:-$(command -v godot || command -v godot4 || command -v Godot || true)}"
if [ -z "$GODOT" ] && [ -x "/Applications/Godot_mono.app/Contents/MacOS/Godot" ]; then GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"; fi
[ -n "$GODOT" ] || { echo "error: Godot 4.3 .NET not found. Download the .NET edition from https://godotengine.org/download and set GODOT=/path/to/it" >&2; exit 1; }

ver="$("$GODOT" --version 2>/dev/null || true)"
case "$ver" in *mono*|*net*) ;; *) echo "warning: '$ver' does not look like the .NET edition of Godot; C# scripts will not load." >&2;; esac

if [ "${1:-}" != "--skip-tests" ]; then
  echo "== simulation tests"; dotnet test EconGame.sln --nologo -v q
fi
echo "== building game"; dotnet build game/EconomicGame.csproj --nologo -v q
echo "== launching ($ver)"; exec "$GODOT" --path game
