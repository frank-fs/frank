#!/usr/bin/env bash
# E2E thesis test for TicTacToe v7.3.2 semantic discovery.
#
# Gate: naive client navigates a complete game using only HTTP responses.
# No hardcoded URLs beyond --server. If Link headers or ALPS profile are
# missing or wrong, the client cannot move — it cannot reverse-engineer the URL.
#
# Run: bash sample/TicTacToe-v732/test-e2e.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PORT=15221
SERVER="http://localhost:$PORT"

fail() { echo "FAIL: $*" >&2; exit 1; }
pass() { echo "PASS: $*"; }

echo "=== TicTacToe v7.3.2 E2E ==="

# ── Build ─────────────────────────────────────────────────────────────────────
echo ""
echo "--- Build ---"
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build \
    "$REPO_ROOT/sample/TicTacToe-v732/TicTacToe.v732.fsproj" 2>&1 | tail -3

# ── Start server ──────────────────────────────────────────────────────────────
echo ""
echo "--- Starting server on $SERVER ---"
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --no-build \
    --project "$REPO_ROOT/sample/TicTacToe-v732/TicTacToe.v732.fsproj" \
    --urls "$SERVER" &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true" EXIT

for i in $(seq 1 30); do
    if curl -sf "$SERVER/" > /dev/null 2>&1; then break; fi
    sleep 0.5
done
curl -sf "$SERVER/" > /dev/null || fail "Server did not start"

# ── AT1: Naive client completes a full game ───────────────────────────────────
echo ""
echo "--- AT1: Naive client completes a full game ---"
uv run python "$SCRIPT_DIR/naive_client.py" --server "$SERVER" --role X --game at1 &
X_PID=$!
uv run python "$SCRIPT_DIR/naive_client.py" --server "$SERVER" --role O --game at1
wait $X_PID
pass "AT1: both players completed"

# ── AT2: Invalid move returns 422 with vocabulary IRIs ────────────────────────
echo ""
echo "--- AT2: Validation rejects illegal moves with vocabulary IRIs ---"
RESP=$(curl -sf -w "\n%{http_code}" -X POST "$SERVER/games/at2/moves" \
    -H "Content-Type: application/ld+json" \
    -d '{"@type":"https://schema.org/MoveAction","https://schema.org/rowIndex":"not-a-number","https://schema.org/columnIndex":0,"https://schema.org/agent":"X"}' \
    2>/dev/null || echo -e "\n000")
HTTP_CODE=$(echo "$RESP" | tail -1)
BODY=$(echo "$RESP" | head -1)
[ "$HTTP_CODE" = "422" ] || fail "AT2: expected 422, got $HTTP_CODE"
echo "$BODY" | grep -q "urn:frank:" && fail "AT2: response contains urn:frank: URIs"
pass "AT2: 422 returned, no urn:frank: IRIs in body"

# ── AT3: Discovery is load-bearing ────────────────────────────────────────────
echo ""
echo "--- AT3: Discovery is load-bearing ---"
# Verify JSON Home is served and contains a game resource template
JSON_HOME=$(curl -sf "$SERVER/" -H "Accept: application/json-home")
echo "$JSON_HOME" | python3 -c "
import json, sys
d = json.load(sys.stdin)
resources = d.get('resources', {})
assert resources, 'No resources in JSON Home'
for rel, r in resources.items():
    if r.get('href-template') or r.get('href'):
        print(f'  found resource: {rel}')
        sys.exit(0)
print('No resource with href-template or href', file=sys.stderr)
sys.exit(1)
" || fail "AT3: JSON Home missing game resource template"

# Verify Link rel=profile on game response (ALPS discovery)
GAME_HEADERS=$(curl -sf -D - -o /dev/null "$SERVER/games/at3" 2>/dev/null || echo "")
echo "$GAME_HEADERS" | grep -i "link:" | grep -i "profile" > /dev/null \
    || fail "AT3: No Link rel=profile on game response — ALPS not discoverable"
pass "AT3: JSON Home has resource template; game response has Link rel=profile"

# ── AT4: Multi-format content negotiation ─────────────────────────────────────
echo ""
echo "--- AT4: Multi-format negotiation ---"
JSON_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "$SERVER/games/at4" -H "Accept: application/json")
LD_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "$SERVER/games/at4" -H "Accept: application/ld+json")
TURTLE_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "$SERVER/games/at4" -H "Accept: text/turtle")
[ "$JSON_CODE" = "200" ] || fail "AT4: JSON returned $JSON_CODE"
[ "$LD_CODE" = "200" ]   || fail "AT4: LD+JSON returned $LD_CODE"
[ "$TURTLE_CODE" = "200" ] || fail "AT4: Turtle returned $TURTLE_CODE"
pass "AT4: JSON $JSON_CODE, LD+JSON $LD_CODE, Turtle $TURTLE_CODE"

echo ""
echo "=== ALL TESTS PASSED ==="
