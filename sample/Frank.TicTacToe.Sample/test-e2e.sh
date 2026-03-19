#!/usr/bin/env bash
# End-to-end test for TicTacToe sample: extract -> embed -> build -> verify affordance headers
# Usage: ./sample/Frank.TicTacToe.Sample/test-e2e.sh [port]
set -euo pipefail

PORT="${1:-5050}"
BASE="http://localhost:$PORT"
PROJECT="sample/Frank.TicTacToe.Sample/Frank.TicTacToe.Sample.fsproj"
PASS=0
FAIL=0

check_header() {
    local label="$1" url="$2" header="$3" expected="$4"
    local actual
    actual=$(curl -s -D - "$url" 2>/dev/null | grep -i "^${header}:" | sed "s/^${header}: //i" | tr -d '\r')
    if echo "$actual" | grep -q "$expected"; then
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected '$expected' in '$actual')"
        FAIL=$((FAIL + 1))
    fi
}

check_no_header() {
    local label="$1" url="$2" header="$3" absent="$4"
    local actual
    actual=$(curl -s -D - "$url" 2>/dev/null | grep -i "^${header}:" | sed "s/^${header}: //i" | tr -d '\r')
    if echo "$actual" | grep -q "$absent"; then
        echo "  FAIL: $label (found '$absent' in '$actual')"
        FAIL=$((FAIL + 1))
    else
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    fi
}

echo "=== Step 1: Extract ==="
dotnet run --project src/Frank.Cli/ -- extract --project "$PROJECT" --base-uri "$BASE/" --force

echo ""
echo "=== Step 2: Build ==="
dotnet build "$PROJECT" -p:EnableSourceLink=false -p:EnableSourceControlManagerQueries=false

echo ""
echo "=== Step 3: Start server ==="
ASPNETCORE_URLS="$BASE" dotnet run --project "$PROJECT" --no-build &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null; wait $SERVER_PID 2>/dev/null" EXIT
sleep 3

echo ""
echo "=== Step 4: Verify affordance headers ==="

# XTurn state: GET + POST allowed, profile link present
echo "XTurn state:"
check_header "Allow includes GET"  "$BASE/games/g1" "Allow" "GET"
check_header "Allow includes POST" "$BASE/games/g1" "Allow" "POST"
check_header "Link has profile"    "$BASE/games/g1" "Link"  "rel=\"profile\""

# Transition to Won: 5 POSTs
for i in 1 2 3 4 5; do curl -s -X POST "$BASE/games/g2" > /dev/null; done

echo "Won state:"
check_header    "Allow includes GET"      "$BASE/games/g2" "Allow" "GET"
check_no_header "Allow excludes POST"     "$BASE/games/g2" "Allow" "POST"
check_header    "Link has profile"        "$BASE/games/g2" "Link"  "rel=\"profile\""

# Body check
echo "State transitions:"
BODY=$(curl -s "$BASE/games/g2")
if echo "$BODY" | grep -q "state=Won"; then
    echo "  PASS: Won state in response body"
    PASS=$((PASS + 1))
else
    echo "  FAIL: Expected 'state=Won' in '$BODY'"
    FAIL=$((FAIL + 1))
fi

echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
[ "$FAIL" -eq 0 ] && exit 0 || exit 1
