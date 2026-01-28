# Quickstart: Enhanced Sample Test Validation

**Feature**: 005-fix-sample-tests
**Date**: 2026-01-27

## Overview

This guide explains how to run and extend the enhanced test scripts for Frank.Datastar sample applications.

## Prerequisites

- Bash shell (macOS, Linux, or WSL)
- curl installed
- .NET SDK (to run sample applications)

## Running Tests

### 1. Start a Sample Application

```bash
# Choose one sample to test
dotnet run --project sample/Frank.Datastar.Basic
# OR
dotnet run --project sample/Frank.Datastar.Hox
# OR
dotnet run --project sample/Frank.Datastar.Oxpecker
```

The server starts on port 5000 by default.

### 2. Run the Test Script

In a separate terminal:

```bash
# Run tests for the running sample
./sample/Frank.Datastar.Basic/test.sh

# Or specify a custom port
./sample/Frank.Datastar.Basic/test.sh 5001
```

### 3. Interpret Results

```
========================================
Frank.Datastar.Basic Test Results
========================================

=== Click-to-Edit Tests ===
  [PASS] View contact shows current values
  [PASS] Edit form displays current values
  [FAIL] Save updates persist: Expected "Updated" but got "Joe"

=== Search Tests ===
  [PASS] Search "ap" returns Apple and Apricot
  [FAIL] Search does not clear list: List was empty

========================================
SUMMARY: 8 passed, 2 failed
========================================
```

- `[PASS]`: Test passed
- `[FAIL]`: Test failed with explanation
- Exit code: 0 if all pass, non-zero if any fail

## Test Structure

Each test script follows this structure:

```bash
#!/bin/bash
# Test script for Frank.Datastar.{Sample}

PORT=${1:-5000}
BASE_URL="http://localhost:$PORT"
SAMPLE_NAME="Frank.Datastar.{Sample}"

PASS_COUNT=0
FAIL_COUNT=0

# Test helper function
assert() {
  local name="$1"
  local condition="$2"
  local message="$3"

  if eval "$condition"; then
    echo "  [PASS] $name"
    PASS_COUNT=$((PASS_COUNT + 1))
  else
    echo "  [FAIL] $name: $message"
    FAIL_COUNT=$((FAIL_COUNT + 1))
  fi
}

# Server availability check
check_server() {
  if ! curl -s -o /dev/null "$BASE_URL/"; then
    echo "ERROR: Server not running at $BASE_URL"
    exit 1
  fi
}

# Test sections...

# Summary
echo "========================================"
echo "SUMMARY: $PASS_COUNT passed, $FAIL_COUNT failed"
echo "========================================"
exit $FAIL_COUNT
```

## Adding New Tests

### Basic Content Assertion

```bash
echo "=== My New Tests ==="

# Test that response contains expected content
RESULT=$(curl -s -m 2 "$BASE_URL/endpoint")
assert "Endpoint returns expected content" \
  'echo "$RESULT" | grep -q "expected text"' \
  "Content not found"
```

### State Change Verification

```bash
# Read initial state
BEFORE=$(curl -s -m 2 "$BASE_URL/resource" | grep -c "pattern")

# Perform operation
curl -s -X PUT "$BASE_URL/resource" -d '{"data":"value"}'
sleep 0.5  # Brief delay for processing

# Verify state changed
AFTER=$(curl -s -m 2 "$BASE_URL/resource" | grep -c "pattern")
assert "Operation changed state" \
  '[ "$AFTER" -ne "$BEFORE" ]' \
  "State unchanged: before=$BEFORE, after=$AFTER"
```

### Fire-and-Forget with Verification

```bash
# Send fire-and-forget request
STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/action")
assert "Action accepted" '[ "$STATUS" = "202" ]' "HTTP $STATUS"

# Verify effect via subsequent read
sleep 0.5
RESULT=$(curl -s -m 2 "$BASE_URL/resource")
assert "Action had expected effect" \
  'echo "$RESULT" | grep -q "expected change"' \
  "Change not observed"
```

## Common Issues

### SSE Timeout

SSE endpoints keep connections open. Use `-m 2` (2-second timeout) to capture initial response:

```bash
# Correct: with timeout
RESULT=$(curl -s -m 2 "$BASE_URL/sse-endpoint")

# Wrong: will hang forever
RESULT=$(curl -s "$BASE_URL/sse-endpoint")
```

### Timing Issues

If tests fail intermittently, add a brief delay before verification:

```bash
curl -s -X PUT "$BASE_URL/update"
sleep 0.5  # Allow server to process
RESULT=$(curl -s -m 2 "$BASE_URL/read")
```

### Checking for Absence

To verify something is NOT in the response:

```bash
assert "Item removed" \
  '! echo "$RESULT" | grep -q "deleted item"' \
  "Item still present"
```

## Test Coverage Checklist

Each sample's test script should cover:

- [ ] Server availability check
- [ ] Click-to-edit: view shows values, edit shows values, save persists
- [ ] Search: returns matches, doesn't return non-matches, handles empty
- [ ] Bulk update: changes selected users, doesn't change unselected
- [ ] Item deletion: removes item, returns 404 for non-existent
- [ ] Registration: validation errors, validation success, create, duplicate
- [ ] State isolation: registration doesn't affect contacts
- [ ] 405 responses: incorrect methods rejected
- [ ] Summary with pass/fail counts
- [ ] Non-zero exit on failure
