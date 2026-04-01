## Thesis

ALPS profiles are the semantic contract between server and client. When Frank
parses an ALPS document, transforms it (projection, dual derivation, role
filtering), and re-emits it, the output must preserve all spec-defined
attributes from the input. A client that publishes an ALPS profile with `href`
links to human-readable documentation must get those links back when the profile
is round-tripped through Frank's pipeline. Silent data loss in the semantic
layer breaks trust in the framework's profile handling.

## Current problem

PR #135 introduced typed `AlpsMeta` DU cases (`AlpsRole`, `AlpsGuardExt`,
`AlpsDuality`, `AlpsAvailableInStates`) that classify known ALPS `ext` elements
during parsing. The classification step discards the `href` attribute for typed
cases.

The ALPS spec defines `href` on `ext` as "a reference to a human-readable
document that describes the extension." If an incoming ALPS document includes:

```json
{ "id": "projectedRole", "href": "https://example.com/extensions/projectedRole", "value": "admin" }
```

The `href` is silently dropped. After round-trip (parse → classify → emit),
the output contains:

```json
{ "id": "projectedRole", "value": "admin" }
```

The `AlpsExtension` fallback case preserves `href` for unknown extensions, but
the typed cases (`AlpsRole`, `AlpsDuality`, `AlpsGuardExt`) do not.

## Definition: "round-trip fidelity"

An ALPS document parsed by Frank and re-emitted without semantic transformation
(identity round-trip) must produce output that preserves all spec-defined
attributes from the input. Attributes may be reordered but not dropped.

## Proposed solution

Add `href: string option` to the typed DU cases, or carry it through a shared
field. Update `classifyExtension` to preserve `href` during classification and
`getExtAnnotations` to re-emit it.

## Acceptance tests

### 1. Round-trip preserves href on typed extension cases

```
Input ALPS JSON:
  ext: { "id": "projectedRole", "href": "https://example.com/extensions/projectedRole", "value": "admin" }

Parse → classify → emit

Output ALPS JSON:
  ext includes "href": "https://example.com/extensions/projectedRole"
```

Test each typed case: AlpsRole, AlpsDuality, AlpsGuardExt, AlpsAvailableInStates.
Each must preserve href when present and omit it cleanly when absent.

### 2. Round-trip preserves href on untyped extension cases (regression)

```
Input ALPS JSON:
  ext: { "id": "customExtension", "href": "https://example.com/custom", "value": "foo" }

Parse → classify (falls through to AlpsExtension) → emit

Output ALPS JSON:
  ext includes "href": "https://example.com/custom"
```

This already works — the test ensures it doesn't regress.

### 3. Projected profile preserves href through transformation

```
Input ALPS JSON with role extension including href
→ Projection.projectAll for a specific role
→ Re-emit projected profile

Output ALPS JSON:
  role extension still includes href
```

This tests that href survives not just identity round-trip but actual
transformation through the projection pipeline. If projection strips href,
clients lose documentation links for the extensions they care about most.

## Dependencies

- Independent of: #250, #251, #253, #254
- Relates to: #252 (discovery surface) — served ALPS profiles must be complete;
  missing href links degrade the profile's usefulness to clients

## Expert sources

- **Amundsen** (CRITICAL): typed DU cases drop href, breaking round-trip fidelity
  for spec-defined attributes
- **Miller** (IMPORTANT): ALPS spec requires href preservation; silent data loss
  in profile handling

---

## Instructions

Make the acceptance tests in the issue above pass.

1. Read the issue thoroughly — the thesis and acceptance tests are the spec
2. Follow TDD (`superpowers:test-driven-development`): write a failing test for each acceptance criterion FIRST, then implement to make it pass
3. Run `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln` and `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` to verify nothing is broken
4. Run the E2E test if one exists
5. Do not claim done without build + test evidence in your output
