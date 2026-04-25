---
name: Feature request
about: A new capability, extension, or behaviour change
labels: enhancement
---

## Thesis

<!-- One sentence stating what this enables and why it matters.
     Frame it as a claim this issue will prove or demonstrate. -->

## Problem

<!-- What is currently wrong, missing, or incomplete?
     Be specific: which component, which behaviour, which spec requirement. -->

## Proposed solution

<!-- How should this be solved? If there are multiple viable approaches, describe the tradeoffs.
     Keep it design-level — leave file/line implementation details out. -->

## Acceptance tests

<!-- Falsifiable HTTP request/response pairs or test sequences.
     A test is falsifiable if a wrong implementation produces a failing result.
     If the correct output can be produced without the correct mechanism, the test isn't tight enough.
     Include at least one negative test (remove the crutch, prove the real mechanism works). -->

```
REQUEST
→ EXPECTED RESPONSE
```

```
REQUEST (negative case)
→ EXPECTED RESPONSE
```

<!-- Unfakeable: note what would differ if the wrong implementation were used. -->

## Success probability

<!-- Rough estimate (e.g. ~85%) with a one-line rationale. Helps triage and scope. -->

## Dependencies

<!-- Other open issues this builds on or is blocked by.
     List expert sources (RFC sections, spec citations) if applicable. -->
