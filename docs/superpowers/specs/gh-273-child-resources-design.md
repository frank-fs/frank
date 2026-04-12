---
source: "github issue #273"
title: "Parent/child resource relationships in statefulResource CE"
milestone: "v7.4.0"
state: "OPEN"
type: spec
---

# Parent/child resource relationships in statefulResource CE

> Extracted from [frank-fs/frank#273](https://github.com/frank-fs/frank/issues/273)

**Probability of Successful Implementation: ~70%**
 
This issue improved significantly from the original — the failure-case acceptance tests (AC-3 through AC-5), error model, instance ID resolution strategy, and `StateMachineContext` capability boundary close the gaps that would have allowed a shortcutted implementation. The risk areas: AC-3 and AC-4 (409 responses when region is inactive or event is invalid) depend on the ValidationAlgebra from #257 existing — without it, the implementer must build ad-hoc validation logic, which is exactly the duplication #257 is meant to prevent. The `StateMachineContext` capability boundary is specified in prose but not as a type signature — an implementer must design the interface, and there's enough ambiguity that two implementations could look quite different. The instance ID resolution strategy (shared route parameter names, validated at startup via AC-7) is concrete and testable but doesn't address edge cases: what about composite keys (two parameters)? What about resources where the parent ID comes from a header or body rather than the route?
 
**To raise to ~80%:** Specify the `StateMachineContext` surface area as a type signature (even illustrative). Clarify whether composite route parameters are in scope. Add an AC for the middleware's behavior when the ValidationAlgebra is not available (fallback to runtime error? refuse to start?).
 
> Revision: `childOf` remains a CE operation needed for both hand-authored and generated resource definitions. The issue is strengthened with failure-case acceptance tests, an explicit error model, and instance ID resolution strategy.
 
## Thesis
 
Hierarchical statecharts have composite states with sub-regions managed by different HTTP resources. The relationship between a parent resource (the state machine owner) and child resources (region-specific endpoints) must be declared so the middleware can enforce constraints, share state, and maintain consistency without hand-coded wiring.
 
The `childOf` CE operation serves **both paths**:
* **SCXML-first**: `frank-cli` scaffolds `statefulResource` definitions with `childOf` already wired from the SCXML hierarchy
* **CE-first**: developers hand-author `childOf` when building statecharts directly in F#
 
## Current problem
 
* No declared relationship between parent and child resources
* Child resources must manually resolve the parent's state store and instance
* Role constraint enforcement on hierarchy ops requires duplicating the parent's role-to-state mapping
* The middleware has no way to know that `/orders/{id}/pick` operates on the same state machine as `/orders/{id}`
 
## Definition: "declared parent/child relationship"
 
A child resource declares its parent via a CE operation. The middleware uses this declaration to:
 
1. Resolve constraints from the parent's statechart (whether generated or hand-authored)
2. Share the parent's `IStatechartsStore` and instance ID
3. Enforce role-based access on hierarchy ops using the parent's metadata
 
## Proposed solution
 
Add a `childOf` CE operation to `statefulResource` that links a child resource to its parent's state machine.
 
**Instance ID resolution strategy**: The middleware derives the parent's instance ID by matching route parameter names between parent and child. The parent's route template declares a parameter (e.g., `{id}`); the child's route template must include the same parameter name. The middleware uses this shared parameter to resolve the parent instance. This is validated at startup (and by analyzer rule FRANK004).
 
**Error model**: When a child resource receives a request for an operation that is invalid given the parent's current state:
 
* **Parent machine not in a state where child's region exists** → 409 Conflict with a body indicating the required parent state
* **Event valid for parent but invalid for current region state** → 409 Conflict with a body indicating the current region state
* **Role not authorized for the operation in the parent's statechart** → 403 Forbidden
* **Parent instance not found** → 404 Not Found
 
**StateMachineContext capability boundary for child handlers**: Child handlers receive a `StateMachineContext` that exposes:
 
* `Send(event)` — send an event scoped to the child's region
* `CurrentState` — read-only view of the parent's overall state
* `RegionState` — read-only view of the child's specific region state
* `Affordances` — available transitions for the current role in the child's region
 
Child handlers MUST NOT have access to:
 
* `IStatechartsStore` directly (enforced by analyzer FRANK001)
* Mutation of regions outside their own scope
* The parent's full `ActiveStateConfiguration` (only projected views)
 
### Architectural constraints
 
* Parent/child relationship MUST be declared via CE operation (library), not wired manually in sample startup code
* Constraint enforcement for hierarchy ops MUST use the parent's metadata, not duplicated mappings in the child
* Child resources MUST NOT directly access `IStatechartsStore` — the middleware manages the relationship (enforced by FRANK001)
* Instance ID resolution MUST be based on shared route parameter names, validated at startup
 
## Acceptance Criteria
 
### AC-1: Child resource enforces parent's role constraints
 
```
Given: an order resource with a pick child resource, where the parent's
  statechart assigns pick operations to the Warehouse role
When: POST /orders/o1/pick with X-Role: Customer (no Warehouse role)
Then: 403 Forbidden
When: POST /orders/o1/pick with X-Role: Warehouse
Then: 202 Accepted
Falsifiable by: the 403 case returning 200 or 500 — would mean role
  constraints from the parent are not enforced on the child
```
 
### AC-2: Child resource shares parent's state store
 
```
Given: a pick child resource that sends PickCompleted to the parent's
  fulfillment region
When: POST /orders/o1/pick with X-Role: Warehouse → 202
Then: GET /orders/o1 → 200, fulfillment region shows pick completed
Falsifiable by: GET /orders/o1 showing no change in the fulfillment region
  — would mean the child operated on a separate state instance
```

### AC-3: Child returns 409 when parent's region is not active
 
```
Given: an order in Created state (fulfillment region not yet active)
When: POST /orders/o1/pick with X-Role: Warehouse
Then: 409 Conflict with body indicating the order must be in Fulfilling
  state before pick operations are available
Falsifiable by: the request returning 500 (unhandled exception) or 200
  (silently succeeding without the region being active)
```
 
### AC-4: Child returns 409 when event is invalid for current region state
 
```
Given: an order in Fulfilling state with pick already completed
  (region is in Packing sub-state)
When: POST /orders/o1/pick with X-Role: Warehouse (pick again)
Then: 409 Conflict with body indicating the region is in Packing state
Falsifiable by: the request returning 500 or 200 — would mean the child
  does not validate event legality against the current region state
```
 
### AC-5: Child returns 404 when parent instance does not exist
 
```
Given: no order with ID "nonexistent"
When: POST /orders/nonexistent/pick with X-Role: Warehouse
Then: 404 Not Found
Falsifiable by: the request returning 500 or a different error code
```
 
### AC-6: Startup fails when childOf references nonexistent parent
 
```
Given: a statefulResource with childOf referencing "order" but no resource
  named "order" is registered
When: the application starts
Then: startup fails with a clear error message identifying the missing parent
Falsifiable by: the application starting successfully and failing only when
  a request hits the child resource
```
 
### AC-7: Startup fails when route parameters are mismatched
 
```
Given: parent resource with route "/orders/{orderId}" and child with route
  "/orders/{id}/pick" (different parameter name)
When: the application starts
Then: startup fails with a clear error message identifying the parameter
  mismatch
Falsifiable by: the application starting successfully and failing at request
  time with an incorrect instance ID resolution
```
 
### AC-8: No direct IStatechartsStore access in child handlers
 
```
Given: a child resource handler that attempts to inject IStatechartsStore
When: the project is compiled with Frank.Analyzers installed
Then: FRANK001 diagnostic is reported
Falsifiable by: the code compiling without diagnostics — would mean the
  analyzer does not detect the injection
(Note: this is also enforced at runtime — if a child handler somehow obtains
  the store, the middleware does not provide it in the DI scope for child
  resources)
```
 
## Dependencies
 
* Depends on: #257 (interpreter algebra — the ValidationAlgebra enables AC-3 and AC-4 via dry-run)
* Depends on: #268 (multi-role tiebreaker — child resources may have users with multiple roles)
* Affected by: codegen design issue (SCXML path scaffolds childOf automatically)
* Affected by: Frank.Analyzers (FRANK001-004 validate childOf usage at compile time)
* Affects: #251 (role projection on child resources needs parent relationship)
 
## Expert Sources
 
* **Harel** (review of #251): "sub-resources for hierarchy ops need formal parent relationship"
* **Miller** (review of #251): "cross-resource constraint enforcement requires declared relationships"
