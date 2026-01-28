# Research: Enhanced Sample Test Validation

**Feature**: 005-fix-sample-tests
**Date**: 2026-01-27

## Research Topics

### 1. SSE Response Parsing with curl

**Context**: Sample applications use Server-Sent Events (SSE) for real-time updates. Tests need to capture and validate SSE content.

**Decision**: Use `curl -s -m <timeout>` with timeout to capture SSE response, then parse with grep/sed.

**Rationale**:
- SSE connections stay open indefinitely; timeout forces curl to close after receiving initial content
- The `-m 2` (2-second timeout) is sufficient to receive the initial HTML patch
- curl returns the full response body including SSE event formatting

**Alternatives Considered**:
- `nc` (netcat): More complex, not installed by default on all systems
- Custom SSE client: Overkill for test scripts
- `--max-time` vs `-m`: Same behavior, `-m` is shorter

**Pattern**:
```bash
# Capture SSE response with timeout
RESULT=$(curl -s -m 2 "$BASE_URL/endpoint" 2>/dev/null || true)
# Parse for expected content
if echo "$RESULT" | grep -q "expected content"; then
  echo "PASS"
else
  echo "FAIL - Expected 'expected content', got: $RESULT"
fi
```

### 2. Content Validation vs. Status Code Validation

**Context**: Current tests only check HTTP status codes (e.g., 202 Accepted). This misses behavioral bugs where the status is correct but content is wrong.

**Decision**: Validate both status code AND response content for all meaningful operations.

**Rationale**:
- Status codes confirm the request was handled
- Content validation confirms the correct data was returned/updated
- Both are necessary for complete test coverage

**Pattern**:
```bash
# Get both status and body
STATUS=$(curl -s -o /tmp/response.txt -w "%{http_code}" "$URL")
BODY=$(cat /tmp/response.txt)

# Validate status
if [ "$STATUS" != "202" ]; then
  echo "FAIL (HTTP $STATUS)"
  return
fi

# Validate content
if ! echo "$BODY" | grep -q "expected"; then
  echo "FAIL - Content mismatch"
  return
fi

echo "PASS"
```

### 3. State Verification Pattern

**Context**: Tests need to verify that operations actually change state (e.g., bulk update modifies user status).

**Decision**: Use read-modify-verify pattern: read initial state, perform operation, verify state changed.

**Rationale**:
- Sequential tests can rely on known initial state from seed data
- Verification read confirms persistence, not just response

**Pattern**:
```bash
# 1. Read initial state
BEFORE=$(curl -s -m 2 "$BASE_URL/users" | grep -c "status-active")

# 2. Perform update
curl -s -X PUT "$BASE_URL/users/bulk?status=active" ...

# 3. Verify state changed
AFTER=$(curl -s -m 2 "$BASE_URL/users" | grep -c "status-active")

if [ "$AFTER" -gt "$BEFORE" ]; then
  echo "PASS"
else
  echo "FAIL - Status unchanged"
fi
```

### 4. Handling Fire-and-Forget Endpoints

**Context**: Some endpoints (edit, search, bulk) return 202 and post updates to SSE channel. The update appears in the SSE stream, not the HTTP response.

**Decision**: For fire-and-forget endpoints, the verification comes from a subsequent read operation, not from the 202 response.

**Rationale**:
- 202 Accepted means "request received" not "operation complete"
- The SSE channel receives the actual update
- Since tests are sequential single-user, the next read will reflect the change

**Pattern**:
```bash
# Fire-and-forget edit request
STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/contacts/1/edit")
[ "$STATUS" = "202" ] || echo "FAIL: edit request"

# Verify edit form appeared (read via SSE with short timeout for pending event)
sleep 0.5  # Brief delay for channel to process
RESULT=$(curl -s -m 2 "$BASE_URL/contacts/1" 2>/dev/null || true)
if echo "$RESULT" | grep -q "data-bind:firstName"; then
  echo "PASS - Edit form displayed"
fi
```

### 5. Known Seed Data for Assertions

**Context**: Tests need to assert against expected values. Each sample has seed data.

**Decision**: Document and rely on known seed data from `Program.fs`:

| Entity | ID | Key Values |
|--------|-----|-----------|
| Contact | 1 | firstName="Joe", lastName="Smith", email="joe@smith.org" |
| Users | 1-4 | User 1: Active, User 2: Inactive, User 3: Active, User 4: Inactive |
| Fruits | - | "Apple", "Apricot", "Banana", ... (22 items) |
| Items | 1-4 | "Item 1" through "Item 4" |

**Rationale**:
- Tests are stateful and sequential
- Known seed data allows precise assertions
- Each test builds on previous state

### 6. Test Output Format

**Context**: Tests should clearly report which sample, which test, and pass/fail with details.

**Decision**: Use structured output with counters and summary.

**Pattern**:
```bash
PASS_COUNT=0
FAIL_COUNT=0
SAMPLE_NAME="Frank.Datastar.Basic"

test_case() {
  local name="$1"
  local result="$2"
  if [ "$result" = "PASS" ]; then
    echo "  [PASS] $name"
    PASS_COUNT=$((PASS_COUNT + 1))
  else
    echo "  [FAIL] $name: $result"
    FAIL_COUNT=$((FAIL_COUNT + 1))
  fi
}

# At end
echo "========================================"
echo "$SAMPLE_NAME: $PASS_COUNT passed, $FAIL_COUNT failed"
echo "========================================"
exit $FAIL_COUNT  # Non-zero if any failures
```

### 7. Async Timing Considerations

**Context**: Hox sample may have different rendering timing. Tests should be resilient to minor timing variations.

**Decision**: Use reasonable timeouts (2-3 seconds) and optional brief delays before verification reads.

**Rationale**:
- Longer timeouts increase test reliability without significantly slowing tests
- Brief `sleep 0.5` before verification reads gives channel time to process
- If tests still flaky, indicates a real bug, not a timing issue

**Alternatives Rejected**:
- Polling with retries: Adds complexity; prefer failing fast to identify real issues
- Very long timeouts: Masks actual bugs; 2-3 seconds is sufficient for functioning code

## Summary

No unresolved technical questions. All patterns are well-understood:

1. SSE testing: `curl -s -m 2` with timeout
2. Content validation: Parse response body with grep
3. State verification: Read-modify-verify pattern
4. Fire-and-forget: Verify via subsequent read
5. Seed data: Documented known values for assertions
6. Output format: Structured pass/fail with summary
7. Timing: Reasonable timeouts, brief delays before verification
