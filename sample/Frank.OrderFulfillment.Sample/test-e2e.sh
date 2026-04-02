#!/usr/bin/env bash
# End-to-end test for OrderFulfillment sample.
#
# Covers two issues:
#   Issue #250 — Hierarchy Operational (acceptance tests 1-4)
#   Issue #254 — HTTP Compliance (acceptance tests 5-8)
#   Issue #251 — Role Projection (acceptance tests AT1-AT4 for role projection)
#
# Usage: ./sample/Frank.OrderFulfillment.Sample/test-e2e.sh [port]
# Prerequisites: build the project first with dotnet build; jq required for JSON checks
set -euo pipefail

PORT="${1:-5060}"
BASE="http://localhost:$PORT"
PROJECT="sample/Frank.OrderFulfillment.Sample/Frank.OrderFulfillment.Sample.fsproj"
PASS=0
FAIL=0
SERVER_LOG=$(mktemp)

# Verify jq is available (needed for JSON response checks)
if ! command -v jq &>/dev/null; then
    echo "ERROR: jq is required but not installed. Install with: brew install jq"
    exit 1
fi

# --- Test helpers ---

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

check_status_with_accept() {
    local label="$1" url="$2" method="$3" accept="$4" expected_status="$5"
    local actual_status
    actual_status=$(curl -s -o /dev/null -w "%{http_code}" -X "$method" -H "Accept: $accept" "$url" 2>/dev/null)
    if [ "$actual_status" = "$expected_status" ]; then
        echo "  PASS: $label (HTTP $actual_status)"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected HTTP $expected_status, got $actual_status)"
        FAIL=$((FAIL + 1))
    fi
}

# Check a JSON field value from a GET request (uses Accept: application/json).
check_json() {
    local label="$1" url="$2" jq_expr="$3" expected="$4"
    local body actual
    body=$(curl -s -H "Accept: application/json" "$url" 2>/dev/null)
    actual=$(echo "$body" | jq -r "$jq_expr" 2>/dev/null || echo "JQ_ERROR")
    if [ "$actual" = "$expected" ]; then
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected '$expected', got '$actual')"
        echo "         body: $body"
        FAIL=$((FAIL + 1))
    fi
}

# Check that a JSON array contains a value.
check_json_contains() {
    local label="$1" url="$2" jq_array="$3" expected="$4"
    local body result
    body=$(curl -s -H "Accept: application/json" "$url" 2>/dev/null)
    result=$(echo "$body" | jq -r "$jq_array" 2>/dev/null | grep -c "^${expected}$" || true)
    if [ "$result" -gt 0 ]; then
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected '$expected' in $jq_array)"
        echo "         body: $body"
        FAIL=$((FAIL + 1))
    fi
}

# Check a region status in the regions array.
check_region() {
    local label="$1" url="$2" region_name="$3" expected_status="$4"
    local body actual
    body=$(curl -s -H "Accept: application/json" "$url" 2>/dev/null)
    actual=$(echo "$body" | jq -r ".regions[] | select(.name == \"$region_name\") | .status" 2>/dev/null || echo "NOT_FOUND")
    if [ "$actual" = "$expected_status" ]; then
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected region $region_name=$expected_status, got '$actual')"
        FAIL=$((FAIL + 1))
    fi
}

# Check plain-text body (for non-JSON endpoints like /diagnostics).
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

# Check that a response header is present.
check_header_present() {
    local label="$1" url="$2" method="$3" header="$4"
    local headers found
    headers=$(curl -s -o /dev/null -D - -X "$method" -H "Accept: application/json" "$url" 2>/dev/null)
    found=$(echo "$headers" | grep -ci "^${header}:" || true)
    if [ "$found" -gt 0 ]; then
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label ($header header not found)"
        echo "         headers: $(echo "$headers" | tr '\r' ' ')"
        FAIL=$((FAIL + 1))
    fi
}

# Check that a response header contains an expected value.
check_header_contains() {
    local label="$1" url="$2" method="$3" header="$4" expected="$5"
    local headers actual
    headers=$(curl -s -o /dev/null -D - -X "$method" -H "Accept: application/json" "$url" 2>/dev/null)
    actual=$(echo "$headers" | grep -i "^${header}:" | head -1 | sed "s/^${header}: *//i" | tr -d '\r\n')
    if echo "$actual" | grep -qi "$expected"; then
        echo "  PASS: $label ($header: $actual)"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected '$expected' in $header: '$actual')"
        FAIL=$((FAIL + 1))
    fi
}

# Check Content-Type header matches expected value.
check_content_type() {
    local label="$1" url="$2" accept="$3" expected_ct="$4"
    local headers actual
    headers=$(curl -s -o /dev/null -D - -H "Accept: $accept" "$url" 2>/dev/null)
    actual=$(echo "$headers" | grep -i "^content-type:" | head -1 | sed 's/^content-type: *//i' | tr -d '\r\n')
    if echo "$actual" | grep -qi "$expected_ct"; then
        echo "  PASS: $label (Content-Type: $actual)"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected Content-Type containing '$expected_ct', got '$actual')"
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

# Check that a response header contains an expected value (with optional X-Role header).
# Checks ALL lines of the header (not just the first), since headers like Link can appear multiple times.
check_header() {
    local label="$1" url="$2" header="$3" expected="$4" role="${5:-}"
    local actual
    if [ -n "$role" ]; then
        actual=$(curl -s -D- -H "X-Role: $role" -H "Accept: application/json" "$url" 2>/dev/null | grep -i "^${header}:")
    else
        actual=$(curl -s -D- -H "Accept: application/json" "$url" 2>/dev/null | grep -i "^${header}:")
    fi
    if echo "$actual" | grep -qi "$expected"; then
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected '$expected' in header '$actual')"
        FAIL=$((FAIL + 1))
    fi
}

# Check that a response header does NOT contain a forbidden value (with optional X-Role header).
# Checks ALL lines of the header (not just the first), since headers like Link can appear multiple times.
check_header_absent() {
    local label="$1" url="$2" header="$3" forbidden="$4" role="${5:-}"
    local actual
    if [ -n "$role" ]; then
        actual=$(curl -s -D- -H "X-Role: $role" -H "Accept: application/json" "$url" 2>/dev/null | grep -i "^${header}:")
    else
        actual=$(curl -s -D- -H "Accept: application/json" "$url" 2>/dev/null | grep -i "^${header}:")
    fi
    if echo "$actual" | grep -qi "$forbidden"; then
        echo "  FAIL: $label (found forbidden '$forbidden' in header '$actual')"
        FAIL=$((FAIL + 1))
    else
        echo "  PASS: $label"
        PASS=$((PASS + 1))
    fi
}

# Check HTTP status with X-Role header.
check_status_with_role() {
    local label="$1" url="$2" method="$3" role="$4" expected_status="$5"
    local actual_status
    actual_status=$(curl -s -o /dev/null -w "%{http_code}" -X "$method" -H "X-Role: $role" "$url" 2>/dev/null)
    if [ "$actual_status" = "$expected_status" ]; then
        echo "  PASS: $label (HTTP $actual_status)"
        PASS=$((PASS + 1))
    else
        echo "  FAIL: $label (expected HTTP $expected_status, got $actual_status)"
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
echo "=== #250 AT1: Remove flat crutch — hierarchy works            ==="
echo "================================================================"
echo ""
echo "--- Setup: advance o1 through Pending → Authorize → Capture → Retry ---"

# Pending → Authorize (via main resource POST = PlaceOrder event)
check_status "o1: PlaceOrder"              "$BASE/orders/o1" "POST" "202"
sleep 1
check_json   "o1: now Authorize"           "$BASE/orders/o1" ".state" "Authorize"

# Authorize → Capture (via sub-resource)
curl -s -X POST "$BASE/orders/o1/authorize" > /dev/null
sleep 1
check_json   "o1: now Capture"             "$BASE/orders/o1" ".state" "Capture"

# Capture → Retry (via sub-resource — this records history: Payment → {Capture})
curl -s -X POST "$BASE/orders/o1/retry" > /dev/null
sleep 1
check_json   "o1: now Retry"               "$BASE/orders/o1" ".state" "Retry"

echo ""
echo "--- KEY TEST: Recover from Retry via shallow history (no flat transition exists) ---"
# POST to main resource — middleware dispatches to Retry state's POST handler (handleRecoverFromRetry)
check_status "AT1: retry-recover returns 202" "$BASE/orders/o1" "POST" "202"
sleep 1
check_json   "AT1: shallow history recovered to Capture" "$BASE/orders/o1" ".state" "Capture"

echo ""
echo "================================================================"
echo "=== #250 AT2: AND-state creates observable parallelism        ==="
echo "================================================================"
echo ""
echo "--- Setup: advance o1 to Fulfillment AND-state ---"

# Capture → Fulfillment (via sub-resource)
curl -s -X POST "$BASE/orders/o1/capture" > /dev/null
sleep 1

# Verify AND-state entry: Pick, Pack, Ship all active
check_json   "AT2: Fulfillment entered"    "$BASE/orders/o1" ".state" "Fulfillment"
check_region "AT2: Pick active"            "$BASE/orders/o1" "Pick" "active"
check_region "AT2: Pack active"            "$BASE/orders/o1" "Pack" "active"
check_region "AT2: Ship active"            "$BASE/orders/o1" "Ship" "active"

echo ""
echo "--- Complete Pick region (via stateful sub-resource → middleware CompleteRegion op) ---"
curl -s -X POST "$BASE/orders/o1/pick" > /dev/null
sleep 1
check_region "AT2: Pick complete"          "$BASE/orders/o1" "Pick" "complete"
check_region "AT2: Pack still active"      "$BASE/orders/o1" "Pack" "active"
check_region "AT2: Ship still active"      "$BASE/orders/o1" "Ship" "active"
check_json   "AT2: still Fulfillment"      "$BASE/orders/o1" ".state" "Fulfillment"

echo ""
echo "--- Complete Pack region (via stateful sub-resource → middleware CompleteRegion op) ---"
curl -s -X POST "$BASE/orders/o1/pack" > /dev/null
sleep 1
check_region "AT2: Pack complete"          "$BASE/orders/o1" "Pack" "complete"
check_region "AT2: Ship still active"      "$BASE/orders/o1" "Ship" "active"
check_json   "AT2: still Fulfillment"      "$BASE/orders/o1" ".state" "Fulfillment"

echo ""
echo "--- Complete Ship region (via stateful sub-resource → middleware CompleteRegion op; all done → Shipped) ---"
curl -s -X POST "$BASE/orders/o1/ship" > /dev/null
sleep 1
check_json   "AT2: all regions complete → Shipped" "$BASE/orders/o1" ".state" "Shipped"

echo ""
echo "================================================================"
echo "=== #250 AT3: Entry/exit ordering is LCA-based               ==="
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
# CapturePayment is RestrictedTo PaymentService, so we must send X-Role header.
curl -s -X POST -H "X-Role: PaymentService" "$BASE/orders/o2" > /dev/null         # Capture → Fulfillment (CapturePayment event)
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
echo "=== #250 AT4: Full hierarchical config visible                ==="
echo "================================================================"
echo ""

# Use o3: advance Pending → Authorize and check full config
curl -s -X POST "$BASE/orders/o3" > /dev/null         # Pending → Authorize
sleep 1

# Config should show full ancestor chain: Order, Processing, Payment, Authorize (issue #265)
check_json_contains "AT4: config includes Order"       "$BASE/orders/o3" ".config[]" "Order"
check_json_contains "AT4: config includes Processing"  "$BASE/orders/o3" ".config[]" "Processing"
check_json_contains "AT4: config includes Payment"     "$BASE/orders/o3" ".config[]" "Payment"
check_json_contains "AT4: config includes Authorize"   "$BASE/orders/o3" ".config[]" "Authorize"
check_json          "AT4: state is Authorize"          "$BASE/orders/o3" ".state" "Authorize"

echo ""
echo "================================================================"
echo "=== #254 AT5: 405 always includes Allow header — both paths   ==="
echo "================================================================"
echo ""

# Path 1: PUT is never registered — should return 405 with Allow header
check_status         "AT5a: PUT returns 405"           "$BASE/orders/o3" "PUT" "405"
check_header_present "AT5a: Allow header present"      "$BASE/orders/o3" "PUT" "Allow"

# Path 2: Advance o1 to Delivered (GET-only), then POST should be 405 with Allow: GET
# ConfirmDelivery is RestrictedTo ShippingProvider; use the direct sub-resource to bypass guards.
curl -s -X POST "$BASE/orders/o1/deliver" > /dev/null   # Shipped → Delivered (direct store manipulation)
sleep 1
check_json           "AT5b: o1 now Delivered"          "$BASE/orders/o1" ".state" "Delivered"
check_status         "AT5b: POST returns 405"          "$BASE/orders/o1" "POST" "405"
check_header_contains "AT5b: Allow lists GET"          "$BASE/orders/o1" "POST" "Allow" "GET"

echo ""
echo "================================================================"
echo "=== #254 AT6: 202 responses include Content-Location          ==="
echo "================================================================"
echo ""

# Use o2 which is in Fulfillment — complete a region to get a 202 with Content-Location
check_header_present "AT6: Content-Location on 202"    "$BASE/orders/o2/pick" "POST" "Content-Location"
check_header_contains "AT6: Content-Location points to resource" "$BASE/orders/o2/pack" "POST" "Content-Location" "/orders/o2/pack"

echo ""
echo "================================================================"
echo "=== #254 AT7: Content negotiation per RFC 9110 Section 12     ==="
echo "================================================================"
echo ""

# Accept: application/json → 200 with JSON Content-Type
check_status_with_accept "AT7a: JSON returns 200"      "$BASE/orders/o3" "GET" "application/json" "200"
check_content_type       "AT7a: Content-Type is JSON"  "$BASE/orders/o3" "application/json" "application/json"

# Accept: application/xml → 406 Not Acceptable (XML not supported)
check_status_with_accept "AT7b: XML returns 406"       "$BASE/orders/o3" "GET" "application/xml" "406"

# Accept: text/html → 406 Not Acceptable (HTML not supported)
check_status_with_accept "AT7c: HTML returns 406"      "$BASE/orders/o3" "GET" "text/html" "406"

echo ""
echo "================================================================"
echo "=== #254 AT8: Allow header consistent across response types   ==="
echo "================================================================"
echo ""

# GET response should include Allow header
check_header_present  "AT8a: Allow on GET response"    "$BASE/orders/o3" "GET" "Allow"

# Allow on GET and Allow on 405 should both list the same methods for the same state.
# Use PaymentService role to verify role-scoped consistency; the role header overlay
# re-applies role-scoped Allow on GET responses. The 405 response comes from ASP.NET Core's
# built-in method-not-allowed endpoint (not our route endpoint), so it cannot have role-scoped
# Allow. Both should include the method as the underlying resource supports it.
GET_ALLOW=$(curl -s -o /dev/null -D - -H "Accept: application/json" -H "X-Role: PaymentService" "$BASE/orders/o3" 2>/dev/null | grep -i "^allow:" | head -1 | sed 's/^allow: *//i' | tr -d '\r\n')
PUT_ALLOW=$(curl -s -o /dev/null -D - -X PUT -H "Accept: application/json" -H "X-Role: PaymentService" "$BASE/orders/o3" 2>/dev/null | grep -i "^allow:" | head -1 | sed 's/^allow: *//i' | tr -d '\r\n')

# Both responses should include GET and POST for PaymentService in Authorize state.
# The 405 endpoint doesn't get role overlay, but underlying Allow is the same.
GET_HAS_GET=$(echo "$GET_ALLOW" | grep -c "GET" || true)
PUT_HAS_GET=$(echo "$PUT_ALLOW" | grep -c "GET" || true)

if [ "$GET_HAS_GET" -gt 0 ] && [ "$PUT_HAS_GET" -gt 0 ] && [ -n "$GET_ALLOW" ]; then
    echo "  PASS: AT8b: Allow includes GET on both GET and PUT-405 (GET: '$GET_ALLOW', PUT-405: '$PUT_ALLOW')"
    PASS=$((PASS + 1))
else
    echo "  FAIL: AT8b: Allow missing GET (GET: '$GET_ALLOW', PUT-405: '$PUT_ALLOW')"
    FAIL=$((FAIL + 1))
fi

echo ""
echo "================================================================"
echo "=== BONUS: AND-state DeriveResult.Warnings (issue #244)       ==="
echo "================================================================"
echo ""
check_body   "Diagnostics: AND-state warnings" "$BASE/diagnostics" "AND-state"
check_status "Diagnostics: GET 200"            "$BASE/diagnostics" "GET" "200"

echo ""
echo "================================================================"
echo "=== ACCEPTANCE TEST: Role Projection AT1 — Different Allow headers ==="
echo "================================================================"
echo ""

# Setup: advance o4 to Authorize state (PaymentService state)
curl -s -X POST "$BASE/orders/o4" > /dev/null    # Pending -> Authorize
sleep 1
check_json "RP-AT1: o4 now Authorize" "$BASE/orders/o4" ".state" "Authorize"

# PaymentService GET in Authorize → Allow includes POST (authorize-payment is their transition)
check_header "RP-AT1: PaymentService Allow includes POST" "$BASE/orders/o4" "Allow" "POST" "PaymentService"

# Customer GET in Authorize → Allow does NOT include POST (no Customer transitions from Authorize)
check_header_absent "RP-AT1: Customer Allow excludes POST" "$BASE/orders/o4" "Allow" "POST" "Customer"

echo ""
echo "================================================================"
echo "=== ACCEPTANCE TEST: Role Projection AT2 — Different Link headers ==="
echo "================================================================"
echo ""

# Same order (o4) in Authorize state:
# PaymentService GET → Link includes authorize-payment
check_header "RP-AT2: PaymentService Link includes authorize-payment" "$BASE/orders/o4" "Link" "authorize-payment" "PaymentService"

# Customer GET → Link does NOT include authorize-payment
check_header_absent "RP-AT2: Customer Link excludes authorize-payment" "$BASE/orders/o4" "Link" "authorize-payment" "Customer"

echo ""
echo "================================================================"
echo "=== ACCEPTANCE TEST: Role Projection AT3 — Role enforcement ==="
echo "================================================================"
echo ""

# Customer POST in Authorize → 403 (blocked — not their role's transition)
check_status_with_role "RP-AT3: Customer POST blocked" "$BASE/orders/o4" "POST" "Customer" "403"

# PaymentService POST in Authorize → 202 (transition succeeds: AuthorizePayment)
check_status_with_role "RP-AT3: PaymentService POST succeeds" "$BASE/orders/o4" "POST" "PaymentService" "202"
sleep 1
check_json "RP-AT3: o4 now Capture after PaymentService POST" "$BASE/orders/o4" ".state" "Capture"

echo ""
echo "================================================================"
echo "=== ACCEPTANCE TEST: Role Projection AT4 — Projected statecharts ==="
echo "================================================================"
echo ""

# GET /diagnostics?role=Customer → shows Customer's projected transitions
check_body "RP-AT4: Customer projection includes cancel" "$BASE/diagnostics?role=Customer" "CancelOrder"

# GET /diagnostics?role=PaymentService → shows PaymentService's projected transitions
check_body "RP-AT4: PaymentService projection includes authorize" "$BASE/diagnostics?role=PaymentService" "AuthorizePayment"
check_body "RP-AT4: PaymentService projection includes capture" "$BASE/diagnostics?role=PaymentService" "CapturePayment"

# Projections must be structurally different (different transition counts)
CUSTOMER_COUNT=$(curl -s "$BASE/diagnostics?role=Customer" 2>/dev/null | grep -o "transitions=[0-9]*" | cut -d= -f2)
PAYMENT_COUNT=$(curl -s "$BASE/diagnostics?role=PaymentService" 2>/dev/null | grep -o "transitions=[0-9]*" | cut -d= -f2)

if [ -n "$CUSTOMER_COUNT" ] && [ -n "$PAYMENT_COUNT" ] && [ "$CUSTOMER_COUNT" != "$PAYMENT_COUNT" ]; then
    echo "  PASS: RP-AT4: Customer ($CUSTOMER_COUNT) and PaymentService ($PAYMENT_COUNT) have different transition counts"
    PASS=$((PASS + 1))
else
    echo "  FAIL: RP-AT4: transition counts should differ (Customer=$CUSTOMER_COUNT, PaymentService=$PAYMENT_COUNT)"
    FAIL=$((FAIL + 1))
fi

echo ""
echo "================================================================"
echo "=== Results: $PASS passed, $FAIL failed ==="
echo "================================================================"
[ "$FAIL" -eq 0 ] && exit 0 || exit 1
