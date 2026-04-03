## Thesis

HTTP method semantics are a protocol-level contract. Safe methods (GET) guarantee no side effects; unsafe methods (POST) do not. When Frank maps statechart transitions to HTTP affordances, it must respect this distinction. A read-only observation transition advertised as POST prevents caching, breaks prefetch, and violates RFC 9110 §9.2.1.

## Current problem

`AffordanceMap.fromStatechart` hardcodes `Method = "POST"` for all transitions. A `getGame` or `viewStatus` transition is semantically safe (read-only) but is advertised as POST in Allow headers and Link relations.

## Definition: "correct method mapping"

Transitions carry safe/unsafe semantics. Safe transitions map to GET, unsafe transitions map to POST. The ALPS descriptor `type` attribute (`safe`, `unsafe`, `idempotent`) is the authoritative source when available.

## Proposed solution

Extend `TransitionSpec` (or the `transition` CE operation) with a safety annotation. Derive from ALPS descriptor type when available. Default to `unsafe` (POST) when unspecified — safe is the explicit opt-in.

## Architectural constraints

- Method mapping MUST be computed in `AffordanceMap.fromStatechart` (library), not overridden per-handler in sample code
- Safety semantics MUST flow from the statechart/ALPS metadata, not from handler registration
- Sample handlers MUST NOT set HTTP methods directly to work around incorrect mapping

## Implementation sequence

1. Add safety field to `TransitionSpec` or transition metadata — checkpoint: type compiles, existing tests pass
2. Update `AffordanceMap.fromStatechart` to use safety for method selection — checkpoint: unit tests show GET for safe, POST for unsafe
3. Wire ALPS descriptor type to safety annotation in extraction pipeline — checkpoint: ALPS profile with `type="safe"` produces GET transition
4. Update sample to declare safe transitions — checkpoint: E2E shows GET for observation transitions

## Acceptance tests

### 1. Safe transition maps to GET

```fsharp
// Transition marked safe in statechart
AffordanceMap.fromStatechart extractedStatechart
→ entry for safe transition has Method = "GET"
```

### 2. Unsafe transition maps to POST (default)

```fsharp
// Transition with no safety annotation
AffordanceMap.fromStatechart extractedStatechart
→ entry for unmarked transition has Method = "POST"
```

### 3. ALPS type annotation drives safety

```fsharp
// ALPS descriptor with type="safe"
→ extracted transition carries safe = true
→ affordance entry has Method = "GET"
```

### 4. Allow header reflects correct methods

```
GET /orders/o1 (in state with both safe and unsafe transitions)
→ Allow: GET, POST (not just POST for everything)
```

## Dependencies

- Independent of: #268, #270, #273
- Affects: #251 (role projection needs correct methods per transition)
- Affects: #271 (MayPoll is inherently safe/GET)

## Expert sources

- **Miller** (review of #251): "conflates safe and unsafe transitions"
- **Amundsen** (review of #251): "ALPS descriptors carry safe/unsafe/idempotent type"
### Design (from affordance pipeline exploration)

**Files:**
- `src/Frank.Resources.Model/ResourceTypes.fs` — add `IsSafe: bool` to `TransitionSpec` (default false)
- `src/Frank.Resources.Model/AffordanceTypes.fs` — `AffordanceMap.fromStatechart` line 229: replace `Method = "POST"` with `Method = if t.IsSafe then "GET" else "POST"`
- `src/Frank.Statecharts/StatefulResourceBuilder.fs` — new `safeTransition` CE operation (follows `useX`/`useXWith` naming pattern to avoid overload ambiguity)

**Usage:**
```fsharp
// Existing (unchanged — defaults to unsafe/POST):
transition PlaceOrder Pending Authorize Unrestricted

// New — safe/GET:
safeTransition ViewStatus Authorize Authorize Unrestricted
```

No ALPS integration in this issue — `safeTransition` is the CE-level mechanism. ALPS descriptor type → IsSafe derivation is a follow-up.

---

## Instructions

Make the acceptance tests in the issue above pass.

1. Read the ENTIRE issue — thesis, architectural constraints, anti-shortcuts,
   implementation sequence, and acceptance tests are ALL part of the spec
2. Follow the implementation sequence if one is provided — do not skip phases
3. Respect architectural constraints — if the issue says the solution must be
   in the library, do not hand-code it in the application
4. Check anti-shortcuts before claiming done — if your implementation matches
   a listed anti-shortcut, it is wrong regardless of test results
5. Follow TDD (`superpowers:test-driven-development`): write a failing test
   for each acceptance criterion FIRST, then implement to make it pass
6. Run `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln` and
   `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` to verify nothing is broken
7. Run the E2E test if one exists
8. Do not claim done without build + test evidence in your output
