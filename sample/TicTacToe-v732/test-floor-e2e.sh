#!/usr/bin/env bash
# test-floor-e2e.sh — #372 AT1-AT5 semantic progressive-enhancement floor harness
# Must be run from the worktree root:
#   cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
#   bash sample/TicTacToe-v732/test-floor-e2e.sh

set -u

WORKTREE="$(cd "$(dirname "$0")/../.." && pwd)"
SAMPLE_SRC="$WORKTREE/sample/TicTacToe-v732"
WORK="$WORKTREE/sample/TicTacToe-v732.e2e-work"
FSPROJ="$WORK/TicTacToe.v732.fsproj"
LOCK="$WORK/.frank/semantic-mappings.lock.json"
FRANK_CLI="$WORKTREE/src/Frank.Cli"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

FAILED=0
PASS_COUNT=0

trap 'rm -rf "$WORK"' EXIT

clean_build_dirs() {
    rm -rf "$WORK/obj" "$WORK/bin"
}

pass() {
    echo "AT$1 PASS"
    PASS_COUNT=$((PASS_COUNT + 1))
}

fail() {
    echo "AT$1 FAIL: $2"
    FAILED=1
}

# --- Setup ---

echo "==> Shutting down MSBuild server to flush task DLL cache"
dotnet build-server shutdown

echo "==> Copying sample to $WORK"
cp -r "$SAMPLE_SRC" "$WORK"

# ---------------------------------------------------------------------------
# AT4 — draft fails the gate
# Run BEFORE floor setup: extract only (no finalize), assert MS001.
# ---------------------------------------------------------------------------

echo ""
echo "--- AT4: draft (proposed+unresolved) fails the gate ---"

rm -f "$LOCK" "$WORK/.frank/resolved-move.json"

dotnet run --project "$FRANK_CLI" -- semantic extract \
    --project "$FSPROJ" > /dev/null 2>&1

clean_build_dirs
build_out=$( dotnet build "$FSPROJ" 2>&1 )
build_rc=$?

if [ $build_rc -eq 0 ]; then
    fail 4 "build succeeded on a draft lock; expected MS001 gate failure"
elif echo "$build_out" | grep -q "MS001"; then
    pass 4
else
    fail 4 "build failed but MS001 not found in output. Output: $build_out"
fi

# ---------------------------------------------------------------------------
# Floor setup: extract + finalize → confirmed+excluded only (no Move)
# Reused by AT1, AT2, AT5.
# ---------------------------------------------------------------------------

echo ""
echo "==> Building floor lock (extract + finalize)"

rm -f "$LOCK" "$WORK/.frank/resolved-move.json"

dotnet run --project "$FRANK_CLI" -- semantic extract \
    --project "$FSPROJ" > /dev/null 2>&1

dotnet run --project "$FRANK_CLI" -- semantic finalize \
    --project "$FSPROJ" > /dev/null 2>&1

# ---------------------------------------------------------------------------
# AT1 — floor codegen succeeds (NOT full build)
# ---------------------------------------------------------------------------

echo ""
echo "--- AT1: floor codegen + gate succeeds ---"

# (a) Assert lock has ZERO proposed and ZERO unresolved
proposed_count=$(python3 -c "
import json, sys
d = json.load(open('$LOCK'))
n = sum(1 for m in d['mappings'] if m['status'] in ('proposed', 'unresolved'))
for f in d['mappings']:
    for fld in f.get('fields', []):
        if fld['status'] in ('proposed', 'unresolved'):
            n += 1
print(n)
")

if [ "$proposed_count" -ne 0 ]; then
    fail 1 "finalized lock still has $proposed_count proposed/unresolved entries"
fi

# (b) Run FrankGenerateSemantic target (codegen + gate, no full compile)
clean_build_dirs
codegen_out=$( dotnet build "$FSPROJ" /t:FrankGenerateSemantic 2>&1 )
codegen_rc=$?
AT1_APPROACH="target"

if [ $codegen_rc -ne 0 ]; then
    # Fallback: full build; assert ONLY error is FS0039 mentioning Move
    AT1_APPROACH="fallback"
    clean_build_dirs
    codegen_out=$( dotnet build "$FSPROJ" 2>&1 )
    codegen_rc=$?

    fs0039_count=$(echo "$codegen_out" | grep -c "FS0039" || true)
    move_mention=$(echo "$codegen_out" | grep "FS0039" | grep -c "Move" || true)
    ms001_count=$(echo "$codegen_out" | grep -c "MS001" || true)

    if [ "$ms001_count" -ne 0 ]; then
        fail 1 "fallback build emitted MS001 (gate fired); expected only FS0039"
    elif [ "$fs0039_count" -eq 0 ] || [ "$move_mention" -eq 0 ]; then
        fail 1 "fallback build failed but not with FS0039/Move — unexpected error"
    fi
fi

# (c) Assert GeneratedDiscovery.fs exists
GENERATED_DISCOVERY="$WORK/obj/Debug/net10.0/GeneratedDiscovery.fs"
if [ ! -f "$GENERATED_DISCOVERY" ]; then
    fail 1 "GeneratedDiscovery.fs not found at $GENERATED_DISCOVERY (approach: $AT1_APPROACH)"
else
    # Assert contains schema.org/Game
    if ! grep -q "schema.org/Game" "$GENERATED_DISCOVERY"; then
        fail 1 "GeneratedDiscovery.fs missing schema.org/Game (floor surface incomplete)"
    # Assert does NOT contain schema.org/MoveAction
    elif grep -q "schema.org/MoveAction" "$GENERATED_DISCOVERY"; then
        fail 1 "GeneratedDiscovery.fs contains schema.org/MoveAction on floor (not yet invested)"
    else
        echo "AT1: approach=$AT1_APPROACH; Game present, MoveAction absent"
        if [ "$FAILED" -eq 0 ] || { [ $proposed_count -eq 0 ] && [ -f "$GENERATED_DISCOVERY" ]; }; then
            pass 1
        fi
    fi
fi

# ---------------------------------------------------------------------------
# AT2 — no false assertion (Player excluded; no schema:Play in artifacts)
# ---------------------------------------------------------------------------

echo ""
echo "--- AT2: no false assertion for Player/schema:Play ---"

player_status=$(python3 -c "
import json
d = json.load(open('$LOCK'))
for m in d['mappings']:
    if m['fsharpType'] == 'TicTacToe.Model.Player':
        print(m['status'])
        break
else:
    print('NOT_FOUND')
")

at2_ok=1

if [ "$player_status" != "excluded" ]; then
    fail 2 "TicTacToe.Model.Player has status='$player_status'; expected 'excluded'"
    at2_ok=0
fi

# Check no Generated*.fs artifact contains schema.org/Play or schema:Play
if [ -d "$WORK/obj/Debug/net10.0" ]; then
    play_hits=$(grep -r "schema\.org/Play\|schema:Play" "$WORK/obj/Debug/net10.0/Generated"*.fs 2>/dev/null | wc -l | tr -d ' ')
else
    play_hits=0
fi

if [ "$play_hits" -ne 0 ]; then
    fail 2 "Generated artifact(s) contain schema.org/Play or schema:Play ($play_hits hit(s))"
    at2_ok=0
fi

if [ "$at2_ok" -eq 1 ]; then
    pass 2
fi

# ---------------------------------------------------------------------------
# AT3 — monotonic invest → full build succeeds
# ---------------------------------------------------------------------------

echo ""
echo "--- AT3: accept Move → full build succeeds; MoveAction present ---"

cp "$SAMPLE_SRC/.frank/resolved-move.json" "$WORK/.frank/resolved-move.json"

dotnet run --project "$FRANK_CLI" -- semantic accept \
    --input "$WORK/.frank/resolved-move.json" \
    --source llm \
    --project "$FSPROJ" > /dev/null 2>&1

clean_build_dirs
full_out=$( dotnet build "$FSPROJ" 2>&1 )
full_rc=$?

if [ $full_rc -ne 0 ]; then
    fail 3 "full build failed after accept. Output: $(echo "$full_out" | grep -E 'error|Error' | head -5)"
else
    # Assert MoveAction now present
    if ! grep -q "schema.org/MoveAction" "$GENERATED_DISCOVERY"; then
        fail 3 "GeneratedDiscovery.fs missing schema.org/MoveAction after accept"
    # Assert Game still present (strict superset of AT1)
    elif ! grep -q "schema.org/Game" "$GENERATED_DISCOVERY"; then
        fail 3 "GeneratedDiscovery.fs lost schema.org/Game after accept (not a superset)"
    else
        pass 3
    fi
fi

# ---------------------------------------------------------------------------
# AT5 — excluded passes gate + absent from artifacts
# Reuses floor lock state for lock assertion; uses AT3's GeneratedSemantics.fs
# (the only full-build point) to verify SquareState absent from SemanticResource DU.
# ---------------------------------------------------------------------------

echo ""
echo "--- AT5: excluded type absent from generated SemanticModel DU ---"

squarestate_status=$(python3 -c "
import json
d = json.load(open('$LOCK'))
for m in d['mappings']:
    if m['fsharpType'] == 'TicTacToe.Model.SquareState':
        print(m['status'])
        break
else:
    print('NOT_FOUND')
")

at5_ok=1

if [ "$squarestate_status" != "excluded" ]; then
    fail 5 "TicTacToe.Model.SquareState has status='$squarestate_status'; expected 'excluded'"
    at5_ok=0
fi

GENERATED_SEMANTICS="$WORK/obj/Debug/net10.0/GeneratedSemantics.fs"
if [ ! -f "$GENERATED_SEMANTICS" ]; then
    fail 5 "GeneratedSemantics.fs not found at $GENERATED_SEMANTICS (AT3 full build must have run)"
    at5_ok=0
else
    if grep -q "| SquareState" "$GENERATED_SEMANTICS"; then
        fail 5 "GeneratedSemantics.fs contains '| SquareState' case (excluded type leaked into DU)"
        at5_ok=0
    fi
fi

if [ "$at5_ok" -eq 1 ]; then
    pass 5
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

echo ""
echo "floor-e2e: $PASS_COUNT/5 passed"

exit $FAILED
