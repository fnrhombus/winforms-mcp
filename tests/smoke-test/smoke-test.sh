#!/bin/bash
# Headless mode smoke test — verifies end-to-end MCP flow:
# init → launch_app (headless) → take_screenshot (by PID) → close_app
set -uo pipefail
cd "$(dirname "$0")/../.."

echo "=== MCP Headless Smoke Test ==="
echo ""

# Build first
dotnet build src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj --no-restore -q 2>/dev/null

# Start server as coprocess
export HEADLESS=true
coproc SERVER { dotnet run --no-build --project src/Rhombus.WinFormsMcp.Server/Rhombus.WinFormsMcp.Server.csproj 2>/dev/null; }
SPID=$SERVER_PID

trap "kill $SPID 2>/dev/null || true" EXIT

send() { echo "$1" >&${SERVER[1]}; }
recv() { read -t 15 -r line <&${SERVER[0]}; echo "$line"; }

PASS=0
FAIL=0
ok()   { PASS=$((PASS+1)); echo "  PASS: $1"; }
fail() { FAIL=$((FAIL+1)); echo "  FAIL: $1"; }

# 1. Initialize
echo "[1/5] Initialize..."
send '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"smoke","version":"1.0"}}}'
R=$(recv)
echo "$R" | grep -q '"serverInfo"' && ok "Server initialized" || fail "Initialize failed"
send '{"jsonrpc":"2.0","method":"notifications/initialized"}'

# 2. Launch notepad
echo "[2/5] Launch notepad (headless)..."
send '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"launch_app","arguments":{"path":"notepad.exe"}}}'
R=$(recv)
PID=$(echo "$R" | grep -oP '(pid|\\u0022pid\\u0022):\s*\K[0-9]+' || echo "")
if [ -n "$PID" ]; then
    ok "Notepad launched, PID=$PID"
else
    fail "Could not extract PID from: $(echo "$R" | head -c 200)"
    echo "=== $FAIL FAILED ==="
    exit 1
fi
sleep 3

# 3. Screenshot by PID
echo "[3/5] Screenshot via PrintWindow (pid=$PID)..."
OUTFILE="$(cygpath -w "$(pwd)/tests/smoke-test/headless-notepad.png" 2>/dev/null || pwd | sed 's|^/\([a-z]\)/|\1:/|; s|/|\\\\|g')/tests/smoke-test/headless-notepad.png"
# Simplify: just use a known absolute Windows path
OUTFILE="C:\\\\dev\\\\WinFormsMcp\\\\tests\\\\smoke-test\\\\headless-notepad.png"
rm -f "$OUTFILE"
send "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"take_screenshot\",\"arguments\":{\"pid\":$PID,\"outputPath\":\"$OUTFILE\"}}}"
# Screenshot response can be 400KB+ base64, need longer timeout
R=""
read -t 30 -r R <&${SERVER[0]} || true
echo "  Response length: ${#R} chars"
echo "  Response preview: $(echo "$R" | head -c 500)"

# Check with both forward and backslash paths
echo "  Looking for: $OUTFILE"
if [ -f "$OUTFILE" ]; then
    SIZE=$(wc -c < "$OUTFILE")
    MAGIC=$(xxd -l 4 -p "$OUTFILE")
    if [ "$MAGIC" = "89504e47" ]; then
        ok "Screenshot saved ($SIZE bytes, valid PNG)"
    else
        fail "File exists but not a valid PNG (magic=$MAGIC)"
    fi
else
    fail "Screenshot file not created"
fi

# 4. Close
echo "[4/5] Close notepad..."
send "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"close_app\",\"arguments\":{\"pid\":$PID,\"force\":true}}}"
R=$(recv)
echo "$R" | grep -q 'success' && ok "Notepad closed" || fail "Close failed: $(echo "$R" | head -c 200)"

# 5. Verify
echo "[5/5] Verify cleanup..."
sleep 1
if kill -0 "$PID" 2>/dev/null; then
    fail "Process $PID still running"
else
    ok "Process terminated"
fi

echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
[ $FAIL -eq 0 ] && exit 0 || exit 1
