# Hand-Authored Stub Registry

Deepening red-green-refactor. Hand-authored stubs make the outer E2E pass, then
get deleted layer-by-layer (red again) and replaced with real TDD'd code until
the deepest layer is real and every layer is green.

## Discipline (non-negotiable)

- Every hand-authored stub carries the marker `FRANK-STUB(<AT>)` in a comment.
- Format: `FRANK-STUB(AT-Sx): <what it fakes> — replace with <real impl>`.
- An AT is **NOT complete** while any `FRANK-STUB(AT-Sx)` remains in its chain.
- Completion gate (run before claiming any AT done):
  ```bash
  grep -rn "FRANK-STUB" sample/TicTacToe-v732 src/  # must be empty for the AT
  ```

## Layers (outer → deeper)

1. **E2E** (Playwright AT-S1..S6) — the truth. Never stubbed.
2. **Middleware** — Frank.Discovery / Frank.Validation / Frank.LinkedData consuming artifacts.
3. **Generated artifact** — GeneratedX.fs / ALPS / SHACL / discovery config (what the generator emits).
4. **Generator** — `frank semantic` pipeline that produces the artifact. Deepest.

## Active stubs

(none — off-spec Frank.Discovery reverted; rebuilding spec-true per
docs/superpowers/specs/2026-04-20-v732-semantic-discovery-design.md)

## Removed stubs

(none)

## Property-test candidates (check thesis/AC each step; implement against real code)

(to be populated per AT against spec §6 contracts)

