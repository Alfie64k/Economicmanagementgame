#!/usr/bin/env bash
# Boots the Godot project under Xvfb, walks every page and writes PNG screenshots.
# usage: tools/shots.sh [outdir] [extra selftest args...]   (needs Godot 4.3 mono on PATH as $GODOT or default path below)
set -euo pipefail
OUT=${1:-/tmp/shots}; shift || true
GODOT=${GODOT:-/tmp/claude-0/godot/Godot_v4.3-stable_mono_linux_x86_64/Godot_v4.3-stable_mono_linux.x86_64}
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
rm -rf "$OUT"; mkdir -p "$OUT"
(cd "$ROOT/game" && dotnet build EconomicGame.csproj >/dev/null)
RES=${RES:-1600x2600}; LOG=$(mktemp)
xvfb-run -a -s "-screen 0 ${RES}x24" "$GODOT" --path "$ROOT/game" --rendering-driver opengl3 --resolution $RES -- --selftest --tall "--shots=$OUT" "$@" >"$LOG" 2>&1 || true
grep -E "UIFLOW|SELFTEST|Exception|rror CS|Unhandled" "$LOG" || true
ls "$OUT"
# fail on a failed self-test or any exception swallowed by Godot callbacks
if ! grep -q "SELFTEST OK" "$LOG" || grep -qE "System\.[A-Za-z]*Exception" "$LOG"; then echo "UI self-test FAILED (see $LOG)"; exit 1; fi
