# Issue #389 — expert-review remediation (post-V3, before ff-merge)

All three verticals of #389 are implemented + adversarially gated on `feature/389` (tip `7c4eb423`). A 4-expert review (TimBL, Miller, Seemann, @7sharp9) surfaced findings the maintainer has triaged. Fix ALL items below in this worktree, TDD, then it re-gates before merge.

## Worktree discipline
- Worktree `/Users/ryanr/Code/frank/.claude/worktrees/389` (branch `feature/389`). Bash cwd RESETS to main repo between calls — start EVERY command with `cd /Users/ryanr/Code/frank/.claude/worktrees/389 && ...`; verify `git branch --show-current`=`feature/389`. Read tool with absolute worktree paths.
- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` on build/test. Commit to `feature/389` only. No push/merge.
- TDD: failing test per behavioral change FIRST (confirm red for the right reason), then green. Mechanical cleanups (L-items) don't each need a new test but must not break existing ones.
- Keep the exit-code convention: 2=drift/differences, 1=operational, 0=none.

## H1 (MUST) — stamp `Owned` in production so validate/owned-SLA are not inert

`VocabClassifier.isOwnedByAuthority (appBaseUri) (vocabUri)` is pure + tested but called NOWHERE in `src/`. Nothing ever writes `entry.Owned = true`, so for any freshly generated lock: `validate`'s `List.filter (snd >> _.Owned)` is empty ("no owned vocabulary entries") and refresh's `classifyOwned` branch is unreachable. A-C7/A-C8 pass only because test fixtures hand-set `Owned=true`. Fix the wiring:

1. Find where the lock's `VocabularyEntry` values are generated/finalized (start at `Frank.Cli.Core/Pipeline.fs`, `Frank.Cli` `handleFinalize`/`handleExtract`, `Frank.Semantic/VocabularyBuilder.fs`) and where the app's DECLARED BASE URI is available (the `vocabulary` CE / declared prefixes / config the pipeline already reads — the same base the app serves its own vocab under). If no declared base is currently threaded to lock generation, thread it (smallest honest change; do not invent a base).
2. At generation, stamp each entry's `Owned = isOwnedByAuthority appBaseUri entry.Uri` (authority-normalized: http↔https, www↔apex — already in `isOwnedByAuthority`). Honor an explicit `owned` marker from the `vocabulary` CE if one exists (claim, not guarantee) — OR authority-match. Re-stamp integrity after.
3. **Test (red first):** generate/finalize a lock for an app whose declared base is `https://example.org` referencing a self-hosted `https://example.org/vocab#…` AND an external `https://schema.org/…`. Assert the self-hosted entry lands `Owned=true` and the external `Owned=false` — via the real generation path, NOT a hand-set fixture. This is the test that would have caught the inert wiring.
4. Keep the existing A-C7/A-C8 unit fixtures (they legitimately unit-test the SLA/validate branch in isolation), but the new generation test proves owned entries actually arise in production.

If threading the base URI turns out to require a genuine design choice (e.g. the base isn't known at generation time), STOP and report the blocker with what you found rather than inventing a source.

## H2 (document only — do NOT enforce here) — term-level dereferenceability boundary

`entry.Terms` is captured (Validate.fs:49, Refresh.fs:80) but nothing intersects the app's REFERENCED terms against it, so "validated" currently means "namespace serves parseable RDF," not "the referenced terms resolve." The maintainer's decision: term-level enforcement is #378's job (the re-spec'd analyzer owns term-presence/FRANK002; #378 is blocked by #389 and comes next). Do NOT add term-membership checking to #389. Instead:
- Add a clear code comment at `classifyReferencedVocab` (VocabClassifier.fs) stating: classification is namespace-level (does the namespace dereference to RDF); term-level membership (referenced term ∈ `entry.Terms`) is enforced by the #378 analyzer which consumes `entry.Terms`. This records the boundary so "validated" is not misread as term-level.
- No test needed (documentation).

## H3 (MUST) — dispose `HttpResponseMessage` (Constitution rule 6)

`RdfConneg.fs` `sendRequest` returns `Ok resp` and no branch disposes the response (leaks on every 200 and every redirect hop). The request `msg` is correctly `use`-bound; give the response the same treatment. In `fetchLoop`, after the `Ok response` match, add `use _ = response` inside the async block so it disposes on return (covers read-then-return and the recursive redirect case). Verify no double-dispose. Existing tests should stay green.

## M1 (MUST) — 406/415 (and 401/403) are durable "no RDF here", not transient

`handleSuccessResponse`/status mapping sends a 406/415 into the `HttpErrorStatus → TransientFailure` catch-all (exit 1, Validated untouched), while a 200-HTML is `Undereferenceable` (durable, exit 2) — inconsistent for the same "this IRI won't give RDF" fact.
- Map **406 Not Acceptable** and **415** to durable `Undereferenceable` (Validated=false, reason "no RDF representation (406)"), exit 2 for the general/unowned refresh path.
- Map **401/403** to durable `Undereferenceable` with reason "auth-walled (anonymous dereference fails)" — an anonymous follow-your-nose agent cannot resolve it; that is the dereferenceability bar. Make this a clearly-labeled deliberate decision in code (comment), since it's a judgment call.
- Keep 5xx/429/timeout/DNS/TLS as transient (exit 1, Validated untouched).
- **Test:** stub returns 406 to the RDF Accept set → Validated=false + exit 2; a 503 still → transient/exit 1 (regression guard).

## M2 (MUST) — RDFa/`text/html` is unverifiable, NOT durable link-rot

A vocab serving RDFa in `text/html` is currently `NonRdfContent → Undereferenceable → DriftDetected` (exit 2, files a CI issue) — a false "gone" on a followable vocab dotNetRdf just can't parse offline. Add a distinct non-durable state:
- New `VocabState`/evidence outcome `UnverifiableNonRdf` (or equivalent): `Validated=false`, reason "non-RDF media type (possibly RDFa) — not verifiable offline", and it is **NOT** exit-2 drift and **NOT** 410-gone. Map an UNOWNED/external `text/html` 200 here.
- **Preserve A-C7:** for OWNED/self-hosted `validate`, a non-RDF response is still `LyingIri` → Validated=false + exit 2 (you control the endpoint and claim RDF — that IS drift). Only the unowned/external `text/html` case becomes non-durable.
- Genuinely non-RDF non-html (e.g. `application/octet-stream`) for unowned: keep as before (durable) OR fold into UnverifiableNonRdf — pick the smaller change and state which. The key requirement: external `text/html` no longer files a false link-rot issue.
- **Test:** unowned stub serves `text/html` 200 → UnverifiableNonRdf, refresh exit NOT 2 (0 or a non-drift code); owned validate serving `text/html` → still LyingIri + exit 2 (A-C7 regression).

## M3 (MUST) — `verifyIfStamped` must require a stamp for schema v2

`verifyIfStamped` returns `Ok` when `Integrity=None`. That leniency is only legitimate for legacy v1 (whose entries force-default `IsValidated=false`). A hand-authored **v2** lock with `"isValidated":true` and no integrity field passes the CLI trust points (status/refresh/validate) and surfaces as Confirmed — laundering a tamper. Fix: `verifyIfStamped` requires a stamp (calls `verifyIntegrity`) when `SchemaVersion >= 2`; only v1 may be unstamped.
- **Test (red first):** a v2 lock with `Integrity=None` and a validated-true entry → status/refresh/validate report tampered/unstamped and do NOT trust it (exit 1). A legacy v1 unstamped lock still loads (entries unvalidated). Confirm red against current lenient code, then green.

## M4 (MUST) — route the owned path through `buildEvidence` (kill the fork)

`Refresh.fs` `classifyOwned` re-implements durable-vs-transient/`HttpErrorStatus`/`NonRdfContent`/`RedirectCapHit` interpretation independently of `RdfConneg.buildEvidence` (which `classifyUnowned` and `validateOne` both use). This causes the owned/unowned asymmetry (a RedirectCapHit/FetchFailed is durable for unowned but transient for owned) and is duplicated logic (Constitution #8). Refactor `classifyOwned` to be a TRANSFORM over `buildEvidence`'s `EvidenceResult` — suppressing CONTENT-DRIFT for owned (owned content change is not drift) while keeping reachability/rot classification identical to unowned (a 404/redirect-loop on an owned vocab is still durable). After the refactor, owned and unowned must agree on RedirectCapHit/FetchFailed/404/410/406.
- **Test:** owned entry hitting RedirectCapHit (and a 404) classifies durable exit-2, same as unowned; owned content-hash change past SLA is still `EvidenceRefreshed`/not-drift (A-C8 regression).

## M5 (MUST) — accept any 2xx as success, not `status=200` exactly

`elif status = 200` misroutes 203/206 RDF responses to transient. Use `status >= 200 && status < 300`. Test: stub returns 203 with Turtle → Validated=true.

## M6 (SHOULD) — explicit HTTP timeout + bounded wall-clock

`makeNoRedirectClient` sets no `.Timeout` (100s default) and `sendRequest` threads no `CancellationToken`; worst case 100s × 5 hops × N entries sequential with no overall bound (Holzmann #10 applies to wall-clock). Set an explicit per-request `.Timeout` (e.g. 30s, a named constant) on the client. A timeout → transient (exit 1), consistent with the existing timeout mapping. No live-network test needed; a unit assertion that the client carries the timeout is enough.

## M7 (SHOULD) — capture `Terms`/`MediaType` on the owned refresh path

`Refresh.fs` `reachableEntry` bumps FetchedAt/HttpStatus/ETag/LastModified but does NOT set `Terms`/`MediaType`, so an owned vocab refreshed (not validated) keeps `Terms=None`. Since owned content-drift is deliberately not re-checked, capture `Terms`/`MediaType` when the owned reachability probe DOES fetch RDF (past the 90d SLA), so owned term evidence isn't solely dependent on `validate` being run. If you keep it validate-only by design, instead add a code comment stating `validate` is mandatory (not optional) for owned term evidence. Pick one; state which.

## L-items (cleanups — no new test each, keep suite green)

- **L1** `RdfConneg.fs handleRedirect` — do NOT forward `If-None-Match`/`If-Modified-Since` across a redirect hop (RFC 9110 §13.1 — validators are resource-specific). Send conditionals only on the first hop; drop them when recursing to the `Location` target.
- **L2** `Frank.Semantic.Core.fsproj` Description claims "Pure" but `LockFile.read`/`write` do `File.*`. Either move `read`/`write` to the shell (cleanest — they're the only impurity in Core) OR soften the Description to not claim purity for I/O. Prefer softening the Description if moving read/write ripples widely; state your choice.
- **L3** Extract the duplicated `match result with HttpErrorStatus(s,_) -> s | NonRdfContent r -> r.HttpStatus | _ -> 0` (Refresh.fs + Validate.fs) into one pure `statusOf : ConnegFetchResult -> int` in the shared module. (This likely dissolves as part of M4 — if so, note it.)
- **L4** `Uri(entry.Uri)` (Refresh.fs:161, Validate.fs:85, LockFile.buildPrefixMap) is unguarded → `UriFormatException` on a malformed persisted URI escapes the async shell. Model it as a `Result`/outcome (a malformed URI entry → a modeled error/outcome, not a thrown exception).
- **L5** `RdfConneg.fs sendRequest` `try ... with ex -> Error ex.Message` wraps build+send; narrow it to just the send/IO so a non-network defect isn't mislabeled "network error" transient (CLAUDE.md over-broad-catch pattern). At minimum, only `HttpRequestException`/`TaskCanceledException` → transient; let genuine defects surface.
- **L6** `RdfConneg.fs` 304 → `NotModified` unconditionally: only treat 304 as NotModified when a validator was actually sent (prior ETag/Last-Modified present); otherwise a spurious 304 resets the SLA clock on a never-confirmed entry. Also: `status >= 300 && status < 400` includes 300 Multiple Choices — exclude 300 from the followable-redirect range (or handle explicitly). And align the `Accept` header with `isRdfMediaType` (advertise all accepted RDF types with q-values, or trim the accept-list to what's advertised) — pick one and make them consistent.

## After all fixes

- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln` (0 errors), `... dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` (all green, report observed counts), `dotnet fantomas --check src/` (clean). Re-run the A-C1..A-C11 filters to confirm no regression.
- Commit to `feature/389` referencing `#389 (expert-review remediation)`.
- Report per item (H1/H3/M1-M7/L1-L6): what changed (file:line), the red-then-green evidence for the behavioral ones (H1, M1, M2, M3, M4, M5), and honest notes on any item you could not fully close (especially H1 if the base-URI threading hit a design wall, and M7/L2 where you chose between options). Do not paper over gaps.
