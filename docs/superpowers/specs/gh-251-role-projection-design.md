---
source: "github issue #251"
title: "Role-based affordance projection works end-to-end"
milestone: "v7.4.0"
state: "CLOSED"
type: spec
---

# Role-based affordance projection works end-to-end

> Extracted from [frank-fs/frank#251](https://github.com/frank-fs/frank/issues/251)

## Thesis

Frank's MPST role model must produce observable differences in what each role
can see and do. When a client authenticates as PaymentService, it must see only
payment-related affordances. When it authenticates as Customer, it must see only
customer-facing affordances. The affordance projection engine (`Projection.fs`)
exists to compute per-role views — the sample must prove that different roles
produce different HTTP responses from the same endpoint in the same state.

## Current problem

The order fulfillment sample declares 4 MPST roles (Customer, PaymentService,
Warehouse, ShippingProvider) in the `statefulResource` CE, and the domain
documentation claims role-specific obligations ("Customer: MayPoll in most states,
MustSelect to cancel"; "PaymentService: MustSelect to authorize/capture"). But
at runtime, every role sees the same thing:

1. **All transitions use `Constraint = Unrestricted`.** The `ExtractedStatechart`
   built in `computeAndStateWarnings` marks every transition as `Unrestricted`
   (any role can trigger). `Projection.filterTransitionsByRole` treats
   `Unrestricted` as visible to all roles. Every role gets an identical
   projection.

2. **Per-role projection bypasses the projection engine.** Line 327 of
   `Program.fs` builds projections as
   `roleNames |> List.map (fun r -> r, statechart) |> Map.ofList` — the full
   statechart for every role. Even if transitions were `RestrictedTo`, this line
   would bypass `Projection.projectAll` entirely.

3. **No authentication middleware is configured.** The role predicates use
   `user.IsInRole("PaymentService")` etc., but no auth middleware is registered.
   `ClaimsPrincipal.IsInRole` always returns false for unauthenticated requests.
   The Customer role guard defaults to accepting unauthenticated users, so every
   request is effectively "Customer." PaymentService, Warehouse, and
   ShippingProvider never match.

4. **Affordance middleware has no affordance data.** `useAffordances` auto-loads
   from embedded `model.bin`, which doesn't exist in the sample project. The
   middleware resolves zero entries and injects zero Link/Allow headers on
   successful (2xx) responses. Allow headers on 405 responses come from the
   statechart middleware, not the affordance middleware.

The net effect: all 4 roles see identical responses. The MPST role model is
documentation-only, not enforced by the projection engine or observable by
clients.

## Definition: "works end-to-end"

Two clients authenticating as different roles, hitting the same endpoint in the
same state, receive different Allow headers and different Link headers. The
differences match the MPST role obligations: PaymentService sees POST (MustSelect)
in Authorize state, Customer sees only GET (MayPoll) in Authorize state.

## Proposed solution

1. Mark transitions with `RestrictedTo` constraints matching the intended role
   (e.g., `cancelOrder` → `RestrictedTo ["Customer"]`, `authorizePayment` →
   `RestrictedTo ["PaymentService"]`).
2. Use `Projection.projectAll` (or equivalent) to build per-role projections
   instead of duplicating the full statechart.
3. Add basic authentication (even cookie-based or header-based for the sample)
   so role predicates can resolve to different roles.
4. Wire `useAffordancesWith` with an explicit `AffordanceMap` (or generate
   `model.bin` via the CLI) so that 2xx responses carry role-scoped Link and
   Allow headers.

## Acceptance tests

Each test is verified by test-e2e.sh. The issue is not done until every test
produces the specified response.

### 1. Different roles see different Allow headers in the same state

Authenticate as PaymentService and as Customer, both requesting the same order
in Authorize state:

```
GET /orders/o1 -H "X-Role: PaymentService"
→ 200, Allow header includes POST

GET /orders/o1 -H "X-Role: Customer"
→ 200, Allow header does NOT include POST
```

This test is unfakeable: if the affordance middleware returns the same Allow
header regardless of role, this test fails. The only way both assertions pass
is if the projection engine computes different allowed methods per role and the
middleware injects them.

### 2. Different roles see different Link headers in the same state

```
GET /orders/o1 -H "X-Role: PaymentService" (in Authorize state)
→ 200, Link header includes rel for authorize-payment transition

GET /orders/o1 -H "X-Role: Customer" (in Authorize state)
→ 200, Link header includes rel for cancel-order but NOT authorize-payment
```

This test is unfakeable: Link headers must differ by role. If transitions aren't
`RestrictedTo` the correct roles, both clients see the same links.

### 3. Restricted transitions are enforced, not just projected

Authenticate as Customer and attempt to trigger a PaymentService-only transition:

```
POST /orders/o1 -H "X-Role: Customer" (in Authorize state, trying to authorize)
→ 403 or 409 (blocked — not your role's transition)

POST /orders/o1 -H "X-Role: PaymentService" (in Authorize state)
→ 202 (transition succeeds)
```

This test is unfakeable: if role constraints aren't enforced at the guard level,
Customer can trigger PaymentService transitions. The 403/409 proves that the
role model governs behavior, not just what's advertised in headers.

### 4. Role-scoped projection produces different statecharts

```
GET /diagnostics?role=Customer
→ JSON showing Customer's projected transitions (cancel, poll)

GET /diagnostics?role=PaymentService
→ JSON showing PaymentService's projected transitions (authorize, capture, retry)
```

The projections must be structurally different — different transition counts,
different state reachability. This proves `Projection.projectAll` (or equivalent)
is computing per-role views, not duplicating the full statechart.

## Dependencies

- Depends on: #250 (hierarchical transitions operational) — role projection
  must work on top of operational hierarchy, not advisory hierarchy
- Depends on: #242 (hierarchy runtime wiring) — provides middleware dispatch

## Expert sources

- **Miller**: all transitions use `Constraint = Unrestricted`, defeating MPST role projection; per-role projection is identity function bypassing `Projection.projectAll`
- **Amundsen**: role guard predicates are auth-based but sample has no auth; roles are dead code at runtime
- **Claude**: roles declared but inert; `useAffordances` is no-op without `model.bin`; every request treated as Customer
