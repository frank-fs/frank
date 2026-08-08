#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
FIXTURES_DIR="$SCRIPT_DIR/fixtures"
ANALYZER_PATH="$REPO_ROOT/src/Frank.Analyzers/bin/Release/net8.0"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m' # No Color

echo "========================================="
echo "Frank.Analyzers Test Suite"
echo "========================================="
echo ""

# Build analyzer in Release mode
echo "Building analyzer..."
dotnet build "$REPO_ROOT/src/Frank.Analyzers/Frank.Analyzers.fsproj" -c Release -v q

# Build fixtures project
echo "Building fixtures..."
dotnet build "$SCRIPT_DIR/Frank.Analyzers.Tests.fsproj" -c Release -v q

# Ensure fsharp-analyzers tool is available
FSHARP_ANALYZERS="$HOME/.dotnet/tools/fsharp-analyzers"
if [[ ! -f "$FSHARP_ANALYZERS" ]]; then
    echo "Installing fsharp-analyzers tool globally..."
    dotnet tool install -g fsharp-analyzers
fi

echo ""
echo "Running analyzer..."
echo ""

# Run analyzer and capture output
ANALYZER_OUTPUT=$("$FSHARP_ANALYZERS" \
    --project "$SCRIPT_DIR/Frank.Analyzers.Tests.fsproj" \
    --analyzers-path "$ANALYZER_PATH" \
    2>&1) || true

echo "========================================="
echo "Test Results"
echo "========================================="
echo ""

PASSED=0
FAILED=0

# Function to check a test case
check_test() {
    local fixture=$1
    local expect_warning=$2
    local description=$3
    local code=${4:-FRANK001}

    if [[ "$expect_warning" == "true" ]]; then
        if echo "$ANALYZER_OUTPUT" | grep -q "$fixture.fs.*$code"; then
            echo -e "${GREEN}PASS${NC}: $fixture - $description"
            PASSED=$((PASSED + 1))
        else
            echo -e "${RED}FAIL${NC}: $fixture - Expected $code warning ($description)"
            FAILED=$((FAILED + 1))
        fi
    else
        if echo "$ANALYZER_OUTPUT" | grep -q "$fixture.fs.*$code"; then
            echo -e "${RED}FAIL${NC}: $fixture - Unexpected $code warning ($description)"
            FAILED=$((FAILED + 1))
        else
            echo -e "${GREEN}PASS${NC}: $fixture - $description"
            PASSED=$((PASSED + 1))
        fi
    fi
}

# User Story 1: Core duplicate detection
check_test "DuplicateGet" true "Duplicate GET detection"
check_test "ValidSingleHandlers" false "Valid single handlers (no warning)"
check_test "MultipleResources" false "Multiple resources (no warning)"

# User Story 3: All HTTP methods
check_test "DuplicatePost" true "Duplicate POST detection"
check_test "DuplicatePut" true "Duplicate PUT detection"
check_test "DuplicateDelete" true "Duplicate DELETE detection"
check_test "DuplicatePatch" true "Duplicate PATCH detection"
check_test "DuplicateHead" true "Duplicate HEAD detection"
check_test "DuplicateOptions" true "Duplicate OPTIONS detection"
check_test "DuplicateConnect" true "Duplicate CONNECT detection"
check_test "DuplicateTrace" true "Duplicate TRACE detection"
check_test "AllMethodsOnce" false "All methods once (no warning)"

# User Story 4: Datastar compatibility
check_test "DatastarConflict" true "Datastar + GET conflict"
check_test "DatastarWithPost" true "Datastar POST + POST conflict"
check_test "DatastarNoConflict" false "Datastar + POST (no conflict)"

# Duplicate accepts media-type detection
check_test "DuplicateAccepts" true "Duplicate accepts media type detection" "FRANK002"
check_test "DistinctAccepts" false "Distinct accepts media types (no warning)" "FRANK002"

# hrefVar / route template validation
check_test "HrefVarExtra" true "hrefVar with no matching template variable" "FRANK003"
check_test "HrefVarMissing" true "template variable with no hrefVar declaration" "FRANK003"
check_test "HrefVarValid" false "hrefVar matches template variable (no warning)" "FRANK003"
check_test "HrefVarWithLet" false "hrefVar after an intervening let (no warning)" "FRANK003"

# Content checks -- code-only grep (check_test above) can't tell a real
# per-file diagnostic from a filename-keyed fake that emits a canned
# message regardless of the actual mismatch. Require the mismatched name
# to appear in the message text too, matching HrefVarAnalyzer.fs's actual
# createExtraMessage/createMissingMessage format strings.
if echo "$ANALYZER_OUTPUT" | grep -q "HrefVarExtra.fs" && echo "$ANALYZER_OUTPUT" | grep -q "hrefVar 'prodId'"; then
    echo -e "${GREEN}PASS${NC}: HrefVarExtra - message names the mismatched hrefVar 'prodId'"
    PASSED=$((PASSED + 1))
else
    echo -e "${RED}FAIL${NC}: HrefVarExtra - message does not name 'prodId'"
    FAILED=$((FAILED + 1))
fi

if echo "$ANALYZER_OUTPUT" | grep -q "HrefVarMissing.fs" && echo "$ANALYZER_OUTPUT" | grep -q "variable '{id}'"; then
    echo -e "${GREEN}PASS${NC}: HrefVarMissing - message names the missing variable '{id}'"
    PASSED=$((PASSED + 1))
else
    echo -e "${RED}FAIL${NC}: HrefVarMissing - message does not name '{id}'"
    FAILED=$((FAILED + 1))
fi

echo ""
echo "========================================="
echo -e "Total: $((PASSED + FAILED)) | ${GREEN}Passed: $PASSED${NC} | ${RED}Failed: $FAILED${NC}"
echo "========================================="

if [[ $FAILED -gt 0 ]]; then
    echo ""
    echo "Analyzer output for debugging:"
    echo "$ANALYZER_OUTPUT" | grep -E "(FRANK00[12]|Running analyzers)" || true
    exit 1
fi

echo ""
echo "All tests passed!"
exit 0
