#!/usr/bin/env bash
# End-to-end test for OrderFulfillment sample — Hierarchy Operational (issue #250).
#
# Verifies 4 acceptance criteria proving the hierarchy is operational:
#   1. Remove flat crutch — hierarchy still works (shallow history recovery)
#   2. AND-state creates observable parallelism (Pick/Pack/Ship)
#   3. Entry/exit ordering is LCA-based (transition observer output)
#   4. Full hierarchical configuration visible after transition
#
# Usage: ./sample/Frank.OrderFulfillment.Sample/test-e2e.sh [port]
# Prerequisites: build the project first with dotnet build
set -euo pipefail

PORT="${1:-5060}"
BASE="http://localhost:$PORT"
PROJECT="sample/Frank.OrderFulfillment.Sample/Frank.OrderFulfillment.Sample.fsproj"
PASS=0
FAIL=0
SERVER_LOG=$(mktemp)

check_status() {
    local label="$1" url="$2" method="${3:-GET}" expected_status="$4"
    local actual_status
    actual_status=$(curl -s -o /dev/null -w "%{http_code}" -X "$method" "$url" 2>/dev/null)
    if [ "$actual_status" = "$expected_status" ]; then
        echo "  PASS: $label (HTTP $actual_status)"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected HTTP $expected_status, got $actual_status)"
        FAIL=$((FAIL + 1))
    fi
}

check_body() {
    local label="$1" url="$2" expected="$3"
    local actual
    actual=$(curl -s "$url" 2>/dev/null)
    if echo "$actual" | grep -qi "$expected"; then
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected '$expected' in '$actual')"
        FAIL=$((FAIL + 1))
    fi
}

check_body_exact() {
    local label="$1" url="$2" expected="$3"
    local actual
    actual=$(curl -s "$url" 2>/dev/null)
    if echo "$actual" | grep -q "$expected"; then
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected '$expected' in '$actual')"
        FAIL=$((FAIL + 1))
    fi
}

check_log_contains() {
    local label="$1" expected="$2"
    if grep -q "$expected" "$SERVER_LOG"; then
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected '$expected' in server log)"
        FAIL=$((FAIL + 1))
    fi
}

echo "=== Step 1: Build ==="
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build "$PROJECT"

echo ""
echo "=== Step 2: Start server ==="
ASPNETCORE_URLS="$BASE" DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --project "$PROJECT" --no-build 2>"$SERVER_LOG" &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null; wait $SERVER_PID 2>/dev/null; rm -f $SERVER_LOG" EXIT
sleep 4

echo ""
echo "================================================================"
echo "=== ACCEPTANCE TEST 1: Remove flat crutch — hierarchy works  ==="
echo "================================================================"
echo ""
echo "--- Setup: advance o1 through Pending → Authorize → Capture → Retry ---"

# Pending → Authorize (via main resource POST = PlaceOrder event)
check_status "o1: PlaceOrder"              "$BASE/orders/o1" "POST" "202"
sleep 1
check_body   "o1: now Authorize"           "$BASE/orders/o1" "state=Authorize"

# Authorize → Capture (via sub-resource)
curl -s -X POST "$BASE/orders/o1/authorize" > /dev/null
sleep 1
check_body   "o1: now Capture"             "$BASE/orders/o1" "state=Capture"

# Capture → Retry (via sub-resource — this records history: Payment → {Capture})
curl -s -X POST "$BASE/orders/o1/retry" > /dev/null
sleep 1
check_body   "o1: now Retry"               "$BASE/orders/o1" "state=Retry"

echo ""
echo "--- KEY TEST: Recover from Retry via shallow history (no flat transition exists) ---"
curl -s -X POST "$BASE/orders/o1/retry-recover" > /dev/null
sleep 1
check_body   "AT1: shallow history recovered to Capture" "$BASE/orders/o1" "state=Capture"
check_status "AT1: retry-recover returns 202" "$BASE/orders/o1/retry-recover" "POST" "202"

echo ""
echo "================================================================"
echo "=== ACCEPTANCE TEST 2: AND-state creates observable parallelism ==="
echo "================================================================"
echo ""
echo "--- Setup: advance o1 to Fulfillment AND-state ---"

# Capture → Fulfillment (via sub-resource)
curl -s -X POST "$BASE/orders/o1/capture" > /dev/null
sleep 1

# Verify AND-state entry: Pick, Pack, Ship all active
check_body   "AT2: Fulfillment entered"    "$BASE/orders/o1" "state=Fulfillment"
check_body   "AT2: Pick active"            "$BASE/orders/o1" "Pick:active"
check_body   "AT2: Pack active"            "$BASE/orders/o1" "Pack:active"
check_body   "AT2: Ship active"            "$BASE/orders/o1" "Ship:active"

echo ""
echo "--- Complete Pick region ---"
curl -s -X POST "$BASE/orders/o1/pick-complete" > /dev/null
sleep 1
check_body   "AT2: Pick complete"          "$BASE/orders/o1" "Pick:complete"
check_body   "AT2: Pack still active"      "$BASE/orders/o1" "Pack:active"
check_body   "AT2: Ship still active"      "$BASE/orders/o1" "Ship:active"
check_body   "AT2: still Fulfillment"      "$BASE/orders/o1" "state=Fulfillment"

echo ""
echo "--- Complete Pack region ---"
curl -s -X POST "$BASE/orders/o1/pack-complete" > /dev/null
sleep 1
check_body   "AT2: Pack complete"          "$BASE/orders/o1" "Pack:complete"
check_body   "AT2: Ship still active"      "$BASE/orders/o1" "Ship:active"
check_body   "AT2: still Fulfillment"      "$BASE/orders/o1" "state=Fulfillment"

echo ""
echo "--- Complete Ship region (all complete → Shipped) ---"
curl -s -X POST "$BASE/orders/o1/ship-complete" > /dev/null
sleep 1
check_body   "AT2: all regions complete → Shipped" "$BASE/orders/o1" "state=Shipped"

echo ""
echo "================================================================"
echo "=== ACCEPTANCE TEST 3: Entry/exit ordering is LCA-based       ==="
echo "================================================================"
echo ""

# Use o2: advance through Pending → Authorize → Capture → Fulfillment
# The Capture → Fulfillment transition crosses Payment→Fulfillment boundary within Processing.
# LCA = Processing, so exit must include [Capture, Payment], enter must include [Fulfillment, Pick, Pack, Ship]

curl -s -X POST "$BASE/orders/o2" > /dev/null        # Pending → Authorize
sleep 1
curl -s -X POST "$BASE/orders/o2/authorize" > /dev/null   # → Capture
sleep 1

# The key transition: Capture → Fulfillment via the main resource POST
# This goes through the statechart middleware which calls HierarchicalRuntime.transition
# The onTransition observer logs exit/entry to stderr
curl -s -X POST "$BASE/orders/o2" > /dev/null         # Capture → Fulfillment (CapturePayment event)
sleep 2

# Check server log for LCA-based exit/entry (grep for specific log line patterns)
check_log_contains "AT3: exited includes Capture"     "exited:.*Capture"
check_log_contains "AT3: exited includes Payment"     "exited:.*Payment"
check_log_contains "AT3: entered includes Fulfillment" "entered:.*Fulfillment"
check_log_contains "AT3: entered includes Pick"       "entered:.*Pick"
check_log_contains "AT3: entered includes Pack"       "entered:.*Pack"
check_log_contains "AT3: entered includes Ship"       "entered:.*Ship"

echo ""
echo "================================================================"
echo "=== ACCEPTANCE TEST 4: Full hierarchical config visible       ==="
echo "================================================================"
echo ""

# Use o3: advance Pending → Authorize and check full config
curl -s -X POST "$BASE/orders/o3" > /dev/null         # Pending → Authorize
sleep 1

# Config should show full ancestor chain: Order, Processing, Payment, Authorize
check_body   "AT4: config includes Order"       "$BASE/orders/o3" "Order"
check_body   "AT4: config includes Processing"  "$BASE/orders/o3" "Processing"
check_body   "AT4: config includes Payment"     "$BASE/orders/o3" "Payment"
check_body   "AT4: config includes Authorize"   "$BASE/orders/o3" "Authorize"
check_body   "AT4: state is Authorize"          "$BASE/orders/o3" "state=Authorize"

echo ""
echo "================================================================"
echo "=== BONUS: AND-state DeriveResult.Warnings (issue #244)       ==="
echo "================================================================"
echo ""
check_body   "Diagnostics: AND-state warnings" "$BASE/diagnostics" "AND-state"
check_status "Diagnostics: GET 200"            "$BASE/diagnostics" "GET" "200"

echo ""
echo "================================================================"
echo "=== Results: $PASS passed, $FAIL failed ==="
echo "================================================================"
[ "$FAIL" -eq 0 ] && exit 0 || exit 1
