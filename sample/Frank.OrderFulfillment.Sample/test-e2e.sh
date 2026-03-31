#!/usr/bin/env bash
# End-to-end test for OrderFulfillment sample.
# Verifies: useHierarchyWith, hierarchical dispatch, 4 MPST roles,
# XOR composite (Payment: Authorize -> Capture), AND composite (Fulfillment: Pick+Pack+Ship),
# shallow history (Payment retry returns to last step), AND-state DeriveResult.Warnings.
#
# Usage: ./sample/Frank.OrderFulfillment.Sample/test-e2e.sh [port]
# Prerequisites: build the project first with dotnet build
set -euo pipefail

PORT="${1:-5060}"
BASE="http://localhost:$PORT"
PROJECT="sample/Frank.OrderFulfillment.Sample/Frank.OrderFulfillment.Sample.fsproj"
PASS=0
FAIL=0

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

check_header() {
    local label="$1" url="$2" method="${3:-GET}" header="$4" expected="$5"
    local actual
    actual=$(curl -s -D - -o /dev/null -X "$method" "$url" 2>/dev/null | grep -i "^${header}:" | sed "s/^${header}: //i" | tr -d '\r')
    if echo "$actual" | grep -qi "$expected"; then
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected '$expected' in '$actual')"
        FAIL=$((FAIL + 1))
    fi
}

check_no_header_value() {
    local label="$1" url="$2" method="${3:-GET}" header="$4" absent="$5"
    local actual
    actual=$(curl -s -D - -o /dev/null -X "$method" "$url" 2>/dev/null | grep -i "^${header}:" | sed "s/^${header}: //i" | tr -d '\r')
    if echo "$actual" | grep -qi "$absent"; then
        echo "  FAIL: $label (found '$absent' in '$actual')"
        FAIL=$((FAIL + 1))
    else
        echo "  PASS: $label"
        PASS=$((PASS + 1))
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

echo "=== Step 1: Build ==="
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build "$PROJECT"

echo ""
echo "=== Step 2: Start server ==="
ASPNETCORE_URLS="$BASE" DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --project "$PROJECT" --no-build &
SERVER_PID=$!
trap "kill $SERVER_PID 2>/dev/null; wait $SERVER_PID 2>/dev/null" EXIT
sleep 4

echo ""
echo "=== Step 3: Initial state (Pending) ==="

# AC1: order in Pending state responds to GET with 200 and shows state
check_status "Pending: GET returns 200"       "$BASE/orders/o1" "GET" "200"
check_body   "Pending: body shows state"      "$BASE/orders/o1"      "state=Pending"

# AC2: POST in Pending state is allowed (PlaceOrder event)
check_status "Pending: POST returns 202"      "$BASE/orders/o1" "POST" "202"
sleep 1

echo ""
echo "=== Step 4: Authorize state (after PlaceOrder) ==="

check_body   "Authorize: body shows state"    "$BASE/orders/o1" "state=Authorize"
check_status "Authorize: GET returns 200"     "$BASE/orders/o1" "GET"  "200"

# Hierarchical dispatch proof: Authorize state is child of Payment (XOR composite),
# which is child of Processing (XOR composite). GET resolves via hierarchy-aware dispatch.
check_status "Hierarchy dispatch: GET on composite child resolves (200)" "$BASE/orders/o1" "GET" "200"

echo ""
echo "=== Step 5: XOR composite — Authorize -> Capture ==="

# POST /orders/o1/authorize → "authorization succeeded" → direct store sets Capture
curl -s -X POST "$BASE/orders/o1/authorize" > /dev/null
sleep 1
check_body   "Capture after authorize"        "$BASE/orders/o1" "state=Capture"
check_status "Capture: GET returns 200"       "$BASE/orders/o1" "GET"  "200"

echo ""
echo "=== Step 6: Shallow history — Retry returns to Capture ==="

# Trigger retry (Capture -> Retry)
curl -s -X POST "$BASE/orders/o1/retry" > /dev/null
sleep 1
check_body   "Retry state entered"            "$BASE/orders/o1" "state=Retry"

# Recover from retry (shallow history: Retry -> Capture, not Authorize)
curl -s -X POST "$BASE/orders/o1/retry-recover" > /dev/null
sleep 1
check_body   "Shallow history: returned to Capture after retry" "$BASE/orders/o1" "state=Capture"

echo ""
echo "=== Step 7: AND composite — Fulfillment (Pick+Pack+Ship in parallel) ==="

# Capture payment → Fulfillment (AND-state: parallel Pick + Pack + Ship)
curl -s -X POST "$BASE/orders/o1/capture" > /dev/null
sleep 1
check_body   "Fulfillment entered after capture" "$BASE/orders/o1" "state=Fulfillment"
check_status "Fulfillment: GET returns 200"      "$BASE/orders/o1" "GET"  "200"
check_status "Fulfillment: POST returns 202"     "$BASE/orders/o1" "POST" "202"
sleep 1

# After POST in Fulfillment, state transitions to Shipped (FulfillOrder event via statechart middleware)
check_body   "Shipped after FulfillOrder event" "$BASE/orders/o1" "state=Shipped"

echo ""
echo "=== Step 8: Fulfill (via sub-resource) -> Shipped ==="

# Use a second order to verify the fulfill sub-resource path
check_status "Start o4 as Pending"            "$BASE/orders/o4" "GET"  "200"
curl -s -X POST "$BASE/orders/o4/capture" > /dev/null  # Set to Fulfillment directly
sleep 1
check_body   "o4 in Fulfillment"              "$BASE/orders/o4" "state=Fulfillment"
curl -s -X POST "$BASE/orders/o4/fulfill" > /dev/null  # Fulfillment -> Shipped
sleep 1
check_body   "o4 Shipped after fulfill"       "$BASE/orders/o4" "state=Shipped"

echo ""
echo "=== Step 9: 4 MPST roles — Customer cancel ==="

# Customer MustSelect to cancel (Pending state)
check_body   "o2: initial Pending state"      "$BASE/orders/o2" "state=Pending"
curl -s -X POST "$BASE/orders/o2/cancel" > /dev/null
sleep 1
check_body   "o2: Cancelled after cancel"     "$BASE/orders/o2" "state=Cancelled"
check_status "Cancelled: GET returns 200"     "$BASE/orders/o2" "GET"  "200"

# Cancelled is final: PUT should return 405 (method not in statechart)
check_status "Cancelled: PUT returns 405"     "$BASE/orders/o2" "PUT"  "405"

echo ""
echo "=== Step 10: Full lifecycle to Delivered (ShippingProvider role) ==="

# Transition o3 through full lifecycle
curl -s -X POST "$BASE/orders/o3" > /dev/null           # Pending -> Authorize
sleep 1
curl -s -X POST "$BASE/orders/o3/authorize" > /dev/null  # Authorize -> Capture
sleep 1
curl -s -X POST "$BASE/orders/o3/capture" > /dev/null    # Capture -> Fulfillment
sleep 1
curl -s -X POST "$BASE/orders/o3/fulfill" > /dev/null    # Fulfillment -> Shipped
sleep 1
curl -s -X POST "$BASE/orders/o3/deliver" > /dev/null    # Shipped -> Delivered
sleep 1
check_body   "Delivered: state=Delivered"     "$BASE/orders/o3" "state=Delivered"
check_status "Delivered: GET returns 200"     "$BASE/orders/o3" "GET"  "200"
check_status "Delivered: POST returns 405"    "$BASE/orders/o3" "POST" "405"

# Allow header on 405 shows GET is the only allowed method in Delivered state
check_header "Delivered 405 Allow includes GET" "$BASE/orders/o3" "POST" "Allow" "GET"
check_no_header_value "Delivered 405 Allow excludes POST" "$BASE/orders/o3" "POST" "Allow" "POST"

echo ""
echo "=== Step 11: AND-state DeriveResult.Warnings formalism proof (issue #244) ==="
# The server computes AND-state warnings at startup via deriveWithHierarchy.
# The /diagnostics endpoint surfaces these warnings.
# Formalism bound 1 (issue #244): Fulfillment AND-state triggers synchronization barrier warning.
check_body "AND-state warnings present" "$BASE/diagnostics" "AND-state"
check_status "Diagnostics: GET returns 200" "$BASE/diagnostics" "GET" "200"

echo ""
echo "=== Results: $PASS passed, $FAIL failed ==="
[ "$FAIL" -eq 0 ] && exit 0 || exit 1
