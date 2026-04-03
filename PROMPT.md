## Thesis

In MPST, MayPoll is a first-class protocol obligation — a role observing state changes made by other roles. The affordance surface must make observation affordances visible to clients, not just transition affordances. A role with MayPoll obligations but no transitions must still receive guidance on what to do (GET the resource to observe).

Additionally, link relation values must be valid per RFC 8288 §2.1.2 — either IANA-registered or extension relation URIs. Bare kebab-case strings are neither.

## Current problem

Two issues:

1. **MayPoll invisible.** \`AffordanceMap.fromStatechart\` only generates link relations for transitions (MustSelect/POST). A role with MayPoll in a state has zero link rels — indistinguishable from "no affordances." The protocol expects them to poll, but the affordance surface gives no guidance.

2. **Invalid link relation values.** Link rels use bare kebab-case strings (\`authorize-payment\`) which are neither IANA-registered nor valid extension URIs per RFC 8288 §2.1.2. ALPS-based rels should use the ALPS profile fragment URI: \`rel="http://example.com/alps/orders#authorizePayment"\`.

## Definition: "complete affordance surface"

Every MPST obligation (MustSelect AND MayPoll) produces a link relation. MayPoll produces a GET link relation. All link relation values are valid per RFC 8288 — either IANA-registered or extension relation URIs derived from the ALPS profile.

## Proposed solution

1. Generate MayPoll link relations in \`AffordanceMap.fromStatechart\` — Method = "GET", rel derived from state or obligation name.
2. Use ALPS profile fragment URIs as link relation values: \`rel="{profileUri}#{descriptorId}"\` instead of bare kebab-case strings.

## Architectural constraints

- MayPoll link generation MUST be in \`AffordanceMap.fromStatechart\` (library), not hand-coded in sample handlers
- ALPS URI construction MUST be in the affordance pipeline, not per-handler
- Sample handlers MUST NOT add custom Link headers to compensate for missing MayPoll rels

## Implementation sequence

1. Add MayPoll link relation generation to \`AffordanceMap.fromStatechart\` — checkpoint: unit tests show GET rels for MayPoll states
2. Switch link relation values to ALPS profile fragment URIs — checkpoint: unit tests show URI-based rels
3. Update sample to declare ALPS profile base URI — checkpoint: E2E shows correct ALPS fragment rels in Link headers
4. Verify MayPoll roles receive link guidance — checkpoint: Customer in Authorize state gets a GET link rel

## Acceptance tests

### 1. MayPoll role gets observation link relation

```
GET /orders/o1 -H "X-Role: Customer" (in Authorize state — Customer is MayPoll)
→ 200
→ Link header includes a GET-method rel for observing the order
```

Without this, Customer sees zero link rels in Authorize state despite having a protocol obligation.

### 2. MustSelect role still gets transition link relations

```
GET /orders/o1 -H "X-Role: PaymentService" (in Authorize state — PaymentService is MustSelect)
→ 200
→ Link header includes POST-method rel for authorize-payment
```

Existing behavior preserved.

### 3. Link relation values are valid URIs

```
GET /orders/o1
→ Link header rels are ALPS profile fragment URIs
→ e.g., rel="http://example.com/alps/orders#authorizePayment"
→ NOT: rel="authorize-payment"
```

### 4. Bare kebab-case rels no longer appear

```
GET /orders/o1
→ No Link header contains a bare kebab-case rel value
→ All rels are either IANA-registered or valid extension URIs
```

## Dependencies

- Depends on: #269 (safe method transitions — MayPoll rels need GET method mapping)
- Depends on: #270 (toKebabCase — affects descriptor ID formatting)
- Affects: #251 (role projection needs MayPoll rels to show different per-role views)

## Expert sources

- **Amundsen** (review of #251): "MayPoll obligations invisible — roles with no transitions have no affordance guidance"
- **Miller** (review of #251): "kebab-case bare strings are not valid ALPS extension relation URIs per RFC 8288"
### Design (from affordance pipeline exploration)

**MayPoll rels:**

In `AffordanceMap.fromStatechart` (AffordanceTypes.fs), after generating transition rels: for each state, for each role's projection, if the role has zero transitions from that state → add a GET link rel with `rel="monitor"` (IANA-registered, RFC 5765).

**ALPS extension URIs:**

`fromStatechart` already receives `baseUri: string` (added in PR #274). Change rel generation (line 227) from `Rel = toKebabCase t.Event` to `Rel = sprintf "%s#%s" baseUri t.Event`.

Produces: `rel="http://example.com/alps/orders#AuthorizePayment"` — valid RFC 8288 extension URI and dereferenceable ALPS descriptor reference. Keep PascalCase in fragment (ALPS descriptor IDs are PascalCase). Kebab-case moves to display/logging only.

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
