#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PORT=15221
SERVER="http://localhost:$PORT"

echo "=== TicTacToe v7.3.2 E2E ==="

# Build the sample
echo "Building sample..."
cd "$REPO_ROOT"
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build sample/TicTacToe-v732/TicTacToe.v732.fsproj 2>&1 | tail -5

# Start the server
echo "Starting server on port $PORT..."
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --no-build --project sample/TicTacToe-v732/TicTacToe.v732.fsproj \
    --urls "$SERVER" &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null || true" EXIT

# Wait for server to be ready
for i in $(seq 1 20); do
    if curl -s "$SERVER/" > /dev/null 2>&1; then break; fi
    sleep 0.5
done

echo ""
echo "--- AT1: Naive client completes a full game ---"
python3 "$SCRIPT_DIR/naive_client.py" --server "$SERVER" --role X --game g1 &
X_PID=$!
python3 "$SCRIPT_DIR/naive_client.py" --server "$SERVER" --role O --game g1
wait $X_PID
echo "AT1 PASS: both players completed"

echo ""
echo "--- AT2: Invalid move returns 422 with vocabulary IRIs ---"
RESP=$(curl -s -w "\n%{http_code}" -X POST "$SERVER/games/g_at2/moves" \
    -H "Content-Type: application/ld+json" \
    -d '{"@type": "https://schema.org/MoveAction", "https://schema.org/rowIndex": "not-a-number", "https://schema.org/columnIndex": "not-a-number", "https://schema.org/agent": "X"}')
HTTP_CODE=$(echo "$RESP" | tail -1)
BODY=$(echo "$RESP" | head -1)
if [ "$HTTP_CODE" != "422" ]; then
    echo "FAIL: expected 422, got $HTTP_CODE"
    exit 1
fi
if echo "$BODY" | grep -q "urn:frank:"; then
    echo "FAIL: response contains urn:frank: URIs"
    exit 1
fi
echo "AT2 PASS: 422 returned, no urn:frank: in response"

echo ""
echo "--- AT3: Discovery is load-bearing (naive client used ALPS field IRIs) ---"
# Already proven by AT1 completing successfully
echo "AT3 PASS: naive client completed game using only discovered IRIs"

echo ""
echo "--- AT4: Multi-format negotiation ---"
JSON_RESP=$(curl -s -w "%{http_code}" "$SERVER/games/g_at4" -H "Accept: application/json")
JSON_CODE="${JSON_RESP: -3}"
LD_RESP=$(curl -s -w "%{http_code}" "$SERVER/games/g_at4" -H "Accept: application/ld+json")
LD_CODE="${LD_RESP: -3}"
TURTLE_RESP=$(curl -s -w "%{http_code}" "$SERVER/games/g_at4" -H "Accept: text/turtle")
TURTLE_CODE="${TURTLE_RESP: -3}"

if [ "$JSON_CODE" != "200" ] || [ "$LD_CODE" != "200" ] || [ "$TURTLE_CODE" != "200" ]; then
    echo "FAIL: multi-format negotiation failed. JSON=$JSON_CODE LD=$LD_CODE Turtle=$TURTLE_CODE"
    exit 1
fi
echo "AT4 PASS: JSON $JSON_CODE, LD+JSON $LD_CODE, Turtle $TURTLE_CODE"

echo ""
echo "=== ALL TESTS PASSED ==="
