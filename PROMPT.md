## Thesis

The shared AST (StatechartDocument, TransitionEdge, TransitionSpec) is the unified representation across all formats. Any information that exists in a supported format but has no home in the AST is silently lost during cross-format merge. Three categories of metadata are currently missing, causing lossy transformations and limiting downstream features (#288, #290, #304, #306).

## Problem

### Gap 1: No sender/receiver roles on transitions

TransitionEdge has Source/Target — which serve double duty as both state endpoints (SCXML, smcat) and participant identifiers (WSD). The WSD parser (Wsd/Parser.fs:310-311) maps sender→Source and receiver→Target, so standalone WSD round-trips preserve this information.

The gap surfaces during **cross-format merge**. `mergeTransition` (Pipeline.fs:134-140) merges Guard, Action, and Annotations from an enriching document, but structural fields (Source, Target) come from the base document. When SCXML provides state-based Source/Target and WSD provides participant-based Source/Target for the same event, the WSD participant names are discarded — there is no field to hold them alongside the SCXML state names.

```
SCXML edge:  { Source = "Pending"; Target = "Authorize"; Event = "PlaceOrder" }
WSD edge:    { Source = "Customer"; Target = "PaymentService"; Event = "PlaceOrder" }
After merge: { Source = "Pending"; Target = "Authorize"; Event = "PlaceOrder" }
             — "Customer" and "PaymentService" are lost
```

**Impact on v7.4.0:**
- #288 DualAlgebra must infer role obligations from RoleConstraint annotations instead of reading them from the edge
- #306 Role-projected SSE has no way to determine which roles are the "receiver" of a transition
- ALPS generation derives role descriptors from format-specific annotations, not from transition structure

### Gap 2: No message payload types on transitions

TransitionEdge.Parameters is `string list` — untyped parameter names extracted from label syntax (e.g., `PlaceOrder(position)` → `["position"]`). There is no field for the type of the message payload (e.g., `OrderDetails`).

No current parser populates this — it is infrastructure for:
- Future Scribble format support (`PlaceOrder(OrderDetails) from Customer to PaymentService`)
- Promoting ALPS `AlpsDataDescriptor` annotations to first-class AST fields
- Typed event payload generation in #283

Adding it now (as always-None) avoids a second AST-wide change later.

### Gap 3: Composite kind missing from resource model

`StateContainment` (Frank.Resources.Model/ResourceTypes.fs:27-33) intentionally omits XOR/AND composite kind:

```fsharp
type StateContainment =
    { ParentOf: Map<string, string list>
      ChildOf: Map<string, string> }
```

The source comment documents this: "does not carry XOR/AND composite kind information... sufficient for current analyses... insufficient for richer analyses."

`StateHierarchy.toContainment` (Hierarchy.fs:291-292) discards the `StateKind: Map<string, CompositeKind>` during conversion. Downstream consumers in Frank.Resources.Model (Projection, livelock detection) cannot distinguish XOR from AND composites.

**Impact on v7.4.0:**
- #290 CollectorAlgebra needs composite kind to generate correct SCXML (`<state>` vs `<parallel>`)
- #304 Broadcast mechanism needs AND vs XOR to scope event propagation
- FRANK205 AND-state deadlock detection needs composite kind in the analyzable model

## Solution

### Change 1: Add SenderRole/ReceiverRole to TransitionEdge and TransitionSpec

```fsharp
// Frank.Statecharts.Core/Types.fs — TransitionEdge gains:
SenderRole: string option      // who initiates (participant name)
ReceiverRole: string option    // who is affected (participant name)

// Frank.Resources.Model/ResourceTypes.fs — TransitionSpec gains:
SenderRole: string option
ReceiverRole: string option
```

Both fields are optional. The WSD parser populates them from the sender/receiver participant names it already extracts. SCXML, smcat, and XState parsers set them to None. `mergeTransition` propagates them using the established "fill None from enriching" pattern alongside Guard and Action.

### Change 2: Add PayloadType to TransitionEdge

```fsharp
// Frank.Statecharts.Core/Types.fs — TransitionEdge gains:
PayloadType: string option     // type name (e.g., "OrderDetails")
```

Optional. No current parser populates this (all set to None). Coexists with `Parameters: string list` — Parameters holds parameter names, PayloadType holds the type. Added now to avoid a second 66-site AST change later.

### Change 3: Add CompositeKind to StateContainment

```fsharp
// Frank.Resources.Model/ResourceTypes.fs — new DU:
type CompositeKind = XOR | AND

// StateContainment gains:
CompositeKinds: Map<string, CompositeKind>
```

CompositeKind is defined as a 2-case DU in Frank.Resources.Model, separate from `Frank.Statecharts.CompositeKind` in Hierarchy.fs. This is intentional type duplication (2 lines) to preserve Frank.Resources.Model's zero-dependency guarantee. `StateHierarchy.toContainment` maps between the two.

## Architectural constraints

- Frank.Resources.Model MUST remain zero-dependency — no project references
- Frank.Statecharts.Core MUST remain zero-dependency
- All new fields MUST be `option` types (backward-compatible: existing code ignores them as None)
- `mergeTransition` MUST use the "fill None from enriching, never override Some" pattern
- The `CompositeKind` DU duplication between packages is intentional and documented

## Implementation sequence

1. **Type changes** (Types.fs, ResourceTypes.fs) — add fields, add DU. Checkpoint: compiler enumerates every construction site via errors.
2. **Fix all compile errors** (66 TransitionEdge sites, ~73 TransitionSpec sites, StateContainment sites) — mechanical: add `= None` / `= Map.empty` at every site. Checkpoint: `dotnet build Frank.sln` succeeds.
3. **Semantic updates** — WSD parser populates SenderRole/ReceiverRole; `mergeTransition` propagates new fields; `toContainment` maps CompositeKinds; `TransitionExtractor.extract` bridges to TransitionSpec; JSON serialization emits new fields. Checkpoint: new tests pass.
4. **Verify** — full build + test + format check. Checkpoint: all green.

## Anti-shortcuts

- Do NOT skip updating `mergeTransition` — compile succeeds without it (the `{ base' with ... }` expression copies None silently) but the semantic behavior is wrong. Test AC-3 catches this.
- Do NOT put `CompositeKind` only in Frank.Statecharts.Core — StateContainment lives in Frank.Resources.Model which has no dependency on Core.
- Do NOT modify the WSD serializer to emit SenderRole/ReceiverRole — WSD format has no syntax for these fields. The serializer continues to use Source/Target.
- Do NOT add a `TransitionEdge.empty` factory — changing the construction pattern across 66 sites is a separate refactoring concern, not in scope.
- Do NOT forget `StatechartDocumentJson.writeTransition` (Cli.Core) — it writes fields explicitly, not by reflection. New fields need explicit `writeOptional` calls.
- Do NOT forget `Frank.Tests` — it is NOT in Frank.sln and must be tested separately.

## Acceptance tests

### AC-1: WSD parser populates SenderRole and ReceiverRole

```
Given: WSD input "Client->Server: doThing"
When: parsed to StatechartDocument
Then: the TransitionEdge has:
  Source = "Client", Target = Some "Server"
  SenderRole = Some "Client", ReceiverRole = Some "Server"
  PayloadType = None
```
Falsifiable by: SenderRole or ReceiverRole being None after WSD parse.

### AC-2: Non-WSD parsers default new fields to None

```
Given: SCXML input with <transition event="submit" target="submitted"/>
When: parsed to StatechartDocument
Then: SenderRole = None, ReceiverRole = None, PayloadType = None
AND: all existing parser round-trip tests pass without modification
```
Falsifiable by: any existing test failing, indicating a construction site was missed.

### AC-3: mergeTransition propagates SenderRole/ReceiverRole from enriching document

```
Given: base edge { Source="Pending"; SenderRole=None }
  AND: enriching edge { Source="Customer"; SenderRole=Some "Customer" }
When: mergeTransition base enriching
Then: result has SenderRole = Some "Customer"

Given: base edge { SenderRole=Some "Admin" }
  AND: enriching edge { SenderRole=Some "Customer" }
When: mergeTransition base enriching
Then: result has SenderRole = Some "Admin" (base wins)
```
Falsifiable by: None not being filled, or Some being overridden.

### AC-4: TransitionExtractor propagates to TransitionSpec

```
Given: a StatechartDocument with a WSD-parsed edge (SenderRole=Some "Client")
When: TransitionExtractor.extract is called
Then: the resulting TransitionSpec has SenderRole = Some "Client"
```
Falsifiable by: TransitionSpec.SenderRole being None.

### AC-5: toContainment preserves CompositeKinds

```
Given: a StateHierarchy with StateKind = { "Active" -> XOR; "Fulfilling" -> AND }
When: StateHierarchy.toContainment is called
Then: result.CompositeKinds = { "Active" -> XOR; "Fulfilling" -> AND }
```
Falsifiable by: CompositeKinds being Map.empty.

### AC-6: StateContainment.ofPairs backward compatibility

```
Given: existing code calling StateContainment.ofPairs [("Parent", ["Child1"; "Child2"])]
When: compiled against the updated type
Then: compiles successfully with CompositeKinds = Map.empty
```
Falsifiable by: compile error at existing call sites.

### AC-7: JSON serialization includes new fields

```
Given: a TransitionEdge with SenderRole = Some "Client"
When: serialized via StatechartDocumentJson.writeTransition
Then: JSON output includes "senderRole": "Client"
```
Falsifiable by: field missing from JSON output.

### AC-8: All existing tests pass (regression)

```
dotnet build Frank.sln → success
dotnet test Frank.sln --filter "FullyQualifiedName!~Sample" → all pass
dotnet test test/Frank.Tests/ → all pass
dotnet fantomas --check src/ → no formatting violations
```

## Dependencies

- Benefits: #288 (DualAlgebra), #290 (CollectorAlgebra), #304 (broadcast), #306 (role-projected SSE)
- Benefits: #283 (codegen — typed event payloads via PayloadType)
- Independent of: #286 (algebra extraction — this is AST, not interpreter)
- Prepares for: Scribble format support (post-v7.4.0)

## Expert sources

- **Harel** (round 3): hierarchy advisory — CompositeKind in resource model enables downstream hierarchy-aware analysis
- **Miller/Amundsen** (round 3): roles decorative, discovery absent — first-class SenderRole/ReceiverRole moves roles from annotation to structure
- **@7sharp9** (round 3): hot-path allocations — new fields are Option types, zero allocation when None
---

---

## Instructions

Make the acceptance tests in the issue above pass.

1. Read the ENTIRE issue — thesis, architectural constraints, anti-shortcuts,
   implementation sequence, and acceptance tests are ALL part of the spec
2. Follow the implementation sequence — do not skip phases
3. Respect architectural constraints — Frank.Resources.Model MUST remain zero-dependency
4. Check anti-shortcuts before claiming done — if your implementation matches
   a listed anti-shortcut, it is wrong regardless of test results
5. Follow TDD (`superpowers:test-driven-development`): write a failing test
   for each acceptance criterion FIRST, then implement to make it pass
6. Run `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln` and
   `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"`
   to verify nothing is broken
7. Run `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Tests/` separately (not in Frank.sln)
8. Run `dotnet fantomas --check src/` for formatting compliance
9. Do not claim done without build + test evidence in your output

## Key files (verified locations)

- `src/Frank.Statecharts.Core/Types.fs:245` — TransitionEdge record definition (add SenderRole, ReceiverRole, PayloadType)
- `src/Frank.Resources.Model/ResourceTypes.fs:27` — StateContainment (add CompositeKinds field + CompositeKind DU)
- `src/Frank.Resources.Model/ResourceTypes.fs:133` — TransitionSpec (add SenderRole, ReceiverRole)
- `src/Frank.Statecharts/Wsd/Parser.fs:309-322` — WSD parser TransitionEdge construction (populate SenderRole/ReceiverRole)
- `src/Frank.Statecharts/Validation/Pipeline.fs:134-140` — mergeTransition (propagate new fields)
- `src/Frank.Statecharts/Hierarchy.fs:291-292` — toContainment (map CompositeKinds)
- `src/Frank.Statecharts/TransitionExtractor.fs:46-58` — extract (bridge to TransitionSpec)
- `src/Frank.Cli.Core/Statechart/StatechartDocumentJson.fs:45-53` — writeTransition (serialize new fields)

## Construction site count (from codebase exploration)

- **TransitionEdge**: 66 construction sites (13 production, 53+ test)
- **TransitionSpec**: ~73 construction sites (2 production paths, rest in tests)
- **StateContainment**: small — mainly ofPairs + empty + toContainment

All are F# record types — every field must be set at every construction site.
The compiler will enumerate every site that needs updating after you add the new fields.
