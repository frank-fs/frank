## Summary

<!-- Bullet points: what changed and why. Be specific about library-level impact
     (new CE operations, middleware behaviour, header changes, format changes).
     Mention any additional work beyond what the linked issue required. -->

-

## Requirements

<!-- For every requirement in the linked issue, state its status.
     "Closes #X" alone is not enough — reviewers need per-requirement accounting. -->

| Requirement | Status | Evidence |
|-------------|--------|----------|
| AT-1: … | Implemented — `File.fs:line` | |
| AT-2: … | Deferred — see #XX | |
| AT-3: … | Blocked by #XX | |

<!-- Status options:
     Implemented — File.fs:line-or-description
     Deferred — rationale and follow-up issue number
     Blocked — blocked-by issue number (do not merge until resolved) -->

## Test evidence

<!-- Paste the actual output. Don't summarise — show the numbers. -->

```
dotnet build Frank.sln              → 
dotnet test Frank.sln (excl. Sample) → N passed, 0 failed
dotnet test test/Frank.Tests/       → N passed, 0 failed
dotnet fantomas --check src/        → 
```

<!-- If sample e2e tests exist and are relevant, include them: -->
<!-- ./sample/…/test-e2e.sh         → N/N passed -->

## Expert review

<!-- If you ran /expert-review, summarise findings and their resolutions.
     All findings are potentially blocking — list them with outcomes. -->

| Expert | Finding | Resolution |
|--------|---------|------------|
| | | |

<!-- Remove this section entirely if you did not run an expert review. -->

## Reviewer checklist

<!-- Steps a reviewer needs to verify manually. Include at least the commands
     from the acceptance tests in the linked issue. -->

- [ ] `dotnet build Frank.sln` — 0 errors
- [ ] `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` — 0 failures
- [ ] `dotnet test test/Frank.Tests/` — 0 failures
- [ ] `dotnet fantomas --check src/` — no changes
- [ ] <!-- Add manual curl or e2e verification steps from the linked issue ATs -->

---

Closes #
