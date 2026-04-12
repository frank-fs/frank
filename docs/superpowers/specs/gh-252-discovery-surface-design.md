---
source: "github issue #252"
title: "Discovery surface is complete"
milestone: "v7.4.0"
state: "OPEN"
type: spec
---

# Discovery surface is complete

> Extracted from [frank-fs/frank#252](https://github.com/frank-fs/frank/issues/252)

## Thesis

Frank's third pillar is "discovery first-class." A naive client — one with no
hardcoded knowledge of the API — must be able to discover the entry point,
understand what resources exist, learn what transitions are available in each
state, and navigate the full order lifecycle using only standard HTTP mechanisms
and ALPS profiles. If a client needs to know URLs, state names, or transition
semantics in advance, discovery is not first-class.

## Current problem

The order fulfillment sample wires `useAffordances` and `useStatecharts` but
does not wire any discovery middleware. Here is what a naive client experiences:

1. **No entry point catalog.** There is no JSON Home endpoint. A client hitting
   the server's root gets nothing that says "orders exist at /orders/{orderId}."
   The client must know the URL template in advance.

2. **No ALPS profile served.** There is no `useDiscovery`, `useProjectedProfiles`,
   or `useDualProfiles` in the pipeline. A client cannot request a semantic
   profile describing what "authorize-payment" or "cancel-order" mean, what
   roles exist, or what state transitions are legal. The `profile` link relation
   is never emitted.

3. **No Link headers on successful responses.** `useAffordances` auto-loads from
   embedded `model.bin`, which doesn't exist in this sample. The middleware
   resolves zero affordance entries. GET 200 responses carry no Link headers at
   all — no `rel="profile"`, no transition links, no self link. The Allow
   headers on 405 responses come from the statechart middleware's error path,
   not from the affordance middleware.

4. **No OPTIONS support.** The sample never wires `useOptionsDiscovery`. An
   `OPTIONS /orders/o1` request falls through to Kestrel's default behavior
   instead of returning the resource's affordances.

The sample demonstrates statechart-based dispatch (pillar 2) but not discovery
(pillar 3). A naive client cannot navigate this API.

## Definition: "complete"

A client with only the server's base URL can:
1. Discover that an order resource exists (via JSON Home or root resource)
2. Learn what the order resource's states and transitions mean (via ALPS profile)
3. See what actions are available in the current state (via Link + Allow headers on every response)
4. Navigate to the next state by following links (no hardcoded URLs)
5. Query capabilities before acting (via OPTIONS)

## Proposed solution

1. Wire `useDiscovery` (or the component parts: `useOptionsDiscovery`,
   `useDiscoveryHeaders`, `useJsonHome`) in the `webHost` CE.
2. Add `<FrankModel>` MSBuild integration to the sample's `.fsproj` so that
   `dotnet build` runs the frank CLI to extract statechart metadata and
   generate `model.bin` as an embedded resource. `useAffordances` auto-loads
   from `model.bin` — no explicit `AffordanceMap` needed.
3. Add a root resource (`/`) that links to the order resource template.

## Acceptance tests

Each test is verified by test-e2e.sh. The issue is not done until every test
produces the specified response.

### 0. model.bin generated from source via frank CLI pipeline

```
dotnet build sample/Frank.OrderFulfillment.Sample/
→ build succeeds
→ embedded resource model.bin exists in the output assembly
→ model.bin contains affordance entries for all statechart states and transitions
```

Verify with:
```
dotnet fsi -e "let a = System.Reflection.Assembly.LoadFrom(\"path.dll\") \
  in a.GetManifestResourceNames() |> Array.iter (printfn \"%s\")"
→ output includes "model.bin"
```

This test is unfakeable: if the MSBuild integration doesn't run the frank CLI,
model.bin won't exist. If the CLI doesn't extract the correct statechart
metadata, the affordance middleware will have wrong or empty entries, and
tests 1-5 will fail (wrong Link headers, missing transitions, etc.).

The pipeline is: source code → frank extract → frank compile → model.bin →
embedded resource → useAffordances auto-loads → Link + Allow headers on
every response. No hand-built AffordanceMap.

### 1. Entry point discovery via JSON Home

```
GET / -H "Accept: application/json-home"
→ 200, body contains a JSON Home document with an entry for the order resource
   including its href-template
```

A naive client starts here. If this returns nothing useful, the client cannot
discover the API.

### 2. ALPS profile is served and linked

```
GET /orders/o1
→ 200, Link header includes rel="profile" pointing to an ALPS profile URL

GET {profile-url}
→ 200, body is a valid ALPS document describing:
   - state descriptors (Pending, Authorize, Capture, etc.)
   - transition descriptors (place-order, authorize-payment, etc.)
   - role descriptors (Customer, PaymentService, Warehouse, ShippingProvider)
```

This test is unfakeable: the profile URL must resolve to a real ALPS document,
and the document must describe the actual states and transitions in the order
resource. A hardcoded profile that doesn't match the statechart would fail when
cross-referenced with the actual Allow/Link headers.

### 3. Link headers on every successful response

```
GET /orders/o1 (in Authorize state, as PaymentService)
→ 200
→ Link header includes self link
→ Link header includes transition link(s) for available actions
→ Allow header includes methods available in this state for this role
```

```
GET /orders/o1 (in Delivered state)
→ 200
→ Link header includes self link
→ Link header does NOT include transition links (terminal state)
→ Allow: GET (only method in terminal state)
```

This test is unfakeable: Link headers come from `model.bin` loaded by
`useAffordances`. If `model.bin` wasn't generated correctly by the frank CLI
(test 0), the affordance entries will be wrong or empty, and these headers
won't appear. The full pipeline from source → CLI → model.bin → runtime
headers must work.

### 4. OPTIONS returns resource capabilities

```
OPTIONS /orders/o1 (in Authorize state)
→ 200
→ Allow header lists methods available in this state
→ Link header includes profile link
```

If `useOptionsDiscovery` isn't wired, this returns Kestrel's default (or 405),
not the resource's actual capabilities.

### 5. Naive client navigation — follow links, not hardcoded URLs

A test sequence where the client:
1. Starts at `/` with no prior knowledge
2. Discovers the order resource from JSON Home
3. Creates/accesses an order
4. Reads the response to find available transitions
5. Follows a link to trigger a transition
6. Reads the new state from the response

No URL is hardcoded in steps 2-6 — every URL comes from a previous response's
Link header or body. If the discovery surface is incomplete, the client gets
stuck.

This is the thesis test for discovery. If a client can navigate the full
lifecycle by following links alone, discovery is complete.

## Dependencies

- Depends on: #251 (role-based affordance projection) — Link headers must be
  role-scoped; discovery without role projection shows everyone the same thing
- Depends on: #250 (hierarchical transitions operational) — discovery must
  reflect operational hierarchy, not advisory
- Aligns with: #254 (HTTP protocol compliance) — content negotiation and
  correct Content-Type are #254's responsibility; this issue covers the
  discovery mechanisms (Link headers, ALPS profiles, JSON Home, OPTIONS)

## Expert sources

- **Claude**: no discovery middleware wired; `useAffordances` is no-op without `model.bin`; thesis is 1/3 demonstrated
- **Miller**: missing machine-readable discovery
- **Amundsen**: no ALPS profile generation or serving for this sample
- **Fielding**: no Link header verification on 2xx responses; no OPTIONS testing
