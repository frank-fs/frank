#!/usr/bin/env bash
set -euo pipefail

WORKTREE=/Users/ryanr/Code/frank/.claude/worktrees/provenance-vertical
PROJ=$WORKTREE/sample/TicTacToe-v732/TicTacToe.v732.fsproj
BASE_URL=http://localhost:5099
GAME_ID=e2e-prov-$(date +%s)
SERVER_PID=

cleanup() {
    if [ -n "$SERVER_PID" ]; then
        kill "$SERVER_PID" 2>/dev/null || true
        wait "$SERVER_PID" 2>/dev/null || true
    fi
}
trap cleanup EXIT

echo "[e2e] Starting server on $BASE_URL (using pre-built binaries)..."
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ASPNETCORE_URLS=$BASE_URL \
    dotnet run --project "$PROJ" --no-build &
SERVER_PID=$!

# Bounded wait: up to 30 tries x 1s = 30s cap
MAX_TRIES=30
try=0
until curl -sf "$BASE_URL/" > /dev/null 2>&1; do
    try=$((try + 1))
    if [ $try -ge $MAX_TRIES ]; then
        echo "[e2e] FAIL: server did not start within ${MAX_TRIES}s" >&2
        exit 1
    fi
    sleep 1
done
echo "[e2e] Server ready after ${try}s"

GAME_URL=$BASE_URL/games/$GAME_ID
MOVES_URL=$BASE_URL/games/$GAME_ID/moves

# Seed the game (GET creates it via GetOrCreate; Content-Type:text/plain avoids ld+json intercept)
echo "[e2e] GET game to create it..."
curl -sf "$GAME_URL" > /dev/null

# --- Assertion 1: POST X move without PROV → seeds lineage record 1 ---
echo "[e2e] POST X move (no PROV header, seeds lineage record)..."
curl -sf \
    -X POST "$MOVES_URL" \
    -H 'Content-Type: application/json' \
    -d '{"position":"TopLeft","player":"X"}' > /dev/null

# --- Assertion 2: POST O move with PROV Accept → body contains prov#Activity + schema:MoveAction ---
echo "[e2e] POST O move with PROV Accept header..."
PROV_RESPONSE=$(curl -sf \
    -X POST "$MOVES_URL" \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/ld+json; profile="http://www.w3.org/ns/prov"' \
    -d '{"position":"MiddleCenter","player":"O"}')

echo "[e2e] PROV response: $PROV_RESPONSE"

if ! echo "$PROV_RESPONSE" | grep -q 'http://www.w3.org/ns/prov#Activity'; then
    echo "[e2e] FAIL: response missing prov#Activity" >&2
    exit 1
fi
echo "[e2e] PASS: prov#Activity present"

if ! echo "$PROV_RESPONSE" | grep -q 'https://schema.org/MoveAction'; then
    echo "[e2e] FAIL: response missing schema:MoveAction" >&2
    exit 1
fi
echo "[e2e] PASS: schema:MoveAction present"

# --- Assertion 3: GET /provenance?resource=... → 200 + at least one prov#Activity ---
echo "[e2e] GET /provenance?resource=$MOVES_URL..."
LINEAGE=$(curl -sf "$BASE_URL/provenance?resource=%2Fgames%2F$GAME_ID%2Fmoves")

echo "[e2e] Lineage response: $LINEAGE"

if ! echo "$LINEAGE" | grep -q 'http://www.w3.org/ns/prov#Activity'; then
    echo "[e2e] FAIL: lineage missing prov#Activity" >&2
    exit 1
fi
echo "[e2e] PASS: lineage contains prov#Activity"

echo "[e2e] ALL ASSERTIONS PASSED"
