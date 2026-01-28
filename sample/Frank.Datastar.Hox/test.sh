#!/bin/bash
# Manual test script for Frank.Datastar.Basic sample application
# Run with: ./test.sh [port]
# Default port is 5000
# Note: SSE endpoints timeout by design (curl exit code 28 is expected)

PORT=${1:-5000}
BASE_URL="http://localhost:$PORT"

echo "========================================"
echo "Frank.Datastar.Basic Manual Tests"
echo "Testing against: $BASE_URL"
echo "========================================"

# Check if server is running
if ! curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/" | grep -q "200"; then
  echo "ERROR: Server not running at $BASE_URL"
  echo "Start it with: dotnet run --project sample/Frank.Datastar.Basic"
  exit 1
fi

echo ""
echo "=== Testing RESTful Resource Patterns ==="

echo -n "11. GET /contacts/1 (SSE view): "
RESULT=$(curl -s -m 2 "$BASE_URL/contacts/1" 2>/dev/null || true)
if echo "$RESULT" | grep -q "First Name"; then
  echo "PASS"
else
  echo "FAIL"
fi

echo -n "12. GET /contacts/1/edit (fire-and-forget): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/contacts/1/edit")
if [ "$STATUS" = "202" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo -n "13. PUT /contacts/1 (update): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X PUT "$BASE_URL/contacts/1" \
  -H "Content-Type: application/json" \
  -d '{"firstName":"John","lastName":"Doe","email":"john@example.com"}')
if [ "$STATUS" = "202" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo -n "14. GET /contacts/99 (404 not found): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -m 2 "$BASE_URL/contacts/99" 2>/dev/null || echo "000")
if [ "$STATUS" = "404" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo -n "15. GET /fruits (SSE list): "
RESULT=$(curl -s -m 2 "$BASE_URL/fruits" 2>/dev/null || true)
if echo "$RESULT" | grep -q "Apple"; then
  echo "PASS"
else
  echo "FAIL"
fi

echo -n "16. GET /fruits?q=ap (search): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/fruits?q=ap")
if [ "$STATUS" = "202" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo -n "17. GET /items (SSE table): "
RESULT=$(curl -s -m 2 "$BASE_URL/items" 2>/dev/null || true)
if echo "$RESULT" | grep -q "items-table"; then
  echo "PASS"
else
  echo "FAIL"
fi

echo -n "18. DELETE /items/99 (404 not found): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X DELETE "$BASE_URL/items/99")
if [ "$STATUS" = "404" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo -n "19. GET /users (SSE table): "
RESULT=$(curl -s -m 2 "$BASE_URL/users" 2>/dev/null || true)
if echo "$RESULT" | grep -q "users-table-container"; then
  echo "PASS"
else
  echo "FAIL"
fi

echo -n "20. PUT /users/bulk (activate): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X PUT "$BASE_URL/users/bulk?status=active" \
  -H "Content-Type: application/json" \
  -d '{"selections":[false,true,false,true]}')
if [ "$STATUS" = "202" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo -n "21. GET /registrations/form (SSE form): "
RESULT=$(curl -s -m 2 "$BASE_URL/registrations/form" 2>/dev/null || true)
if echo "$RESULT" | grep -q "registration-form"; then
  echo "PASS"
else
  echo "FAIL"
fi

echo -n "22. POST /registrations/validate (empty - errors): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/registrations/validate" \
  -H "Content-Type: application/json" \
  -d '{"email":"","firstName":"","lastName":""}')
if [ "$STATUS" = "202" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo -n "23. POST /registrations/validate (valid): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/registrations/validate" \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","firstName":"John","lastName":"Doe"}')
if [ "$STATUS" = "202" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

# Generate unique email to avoid 409 on re-runs
UNIQUE_EMAIL="test-$(date +%s)@example.com"

echo -n "24. POST /registrations (create): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/registrations" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$UNIQUE_EMAIL\",\"firstName\":\"John\",\"lastName\":\"Doe\"}")
if [ "$STATUS" = "201" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo -n "25. POST /registrations (409 duplicate): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/registrations" \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$UNIQUE_EMAIL\",\"firstName\":\"Jane\",\"lastName\":\"Smith\"}")
if [ "$STATUS" = "409" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo ""
echo "=== Testing 405 Method Not Allowed ==="

echo -n "26. DELETE /fruits (405): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X DELETE "$BASE_URL/fruits")
if [ "$STATUS" = "405" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo -n "27. POST /contacts/1 (405): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/contacts/1")
if [ "$STATUS" = "405" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo -n "28. PUT /fruits (405): "
STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X PUT "$BASE_URL/fruits")
if [ "$STATUS" = "405" ]; then
  echo "PASS"
else
  echo "FAIL (HTTP $STATUS)"
fi

echo ""
echo "========================================"
echo "All tests completed!"
echo "========================================"
