# Frank.Rdf: tighten resolveIri's undeclared-prefix fallback

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `resolveIri`'s fallback for an undeclared CURIE prefix currently accepts anything `System.Uri.IsWellFormedUriString` calls "well-formed" under its loose absolute-URI rules — which is true of almost any `word:word` string, so a typo'd prefix like `foaf:name` silently becomes the literal IRI `<foaf:name>` instead of raising. Tighten the fallback to require the string look genuinely like an absolute IRI (contains `://`, or matches a small allow-list of non-hierarchical schemes) before even attempting the well-formedness check.

**Tracks:** frank-fs/frank#484

**Design doc:** `docs/superpowers/specs/2026-07-30-frank-rdf-design.md` (error-handling table)

## Global Constraints

- This is a behavior change to `resolveIri`/`Doc.toGraph` (a fallback that used to accept some strings now raises) — this is the point of the issue, not a regression to work around.
- Do **not** break legitimate absolute-IRI passthrough already covered by existing tests (Wikidata/DBpedia-style `http://...`/`https://...` URLs).
- Test framework is Expecto.
- Commit directly to this task's branch when done (trunk-based repo — no PR needed once merged back to master by the coordinator).

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/Frank.Rdf/Rdf.fs` | Modify | Tighten `resolveIri`'s undeclared-prefix fallback |
| `src/Frank.Rdf/Rdf.fsi` | Modify | Update `resolveIri`'s doc comment to describe the new, stricter guarantee |
| `test/Frank.Rdf.Tests/PrefixResolutionTests.fs` | Modify | New tests for newly-rejected typo'd CURIEs; confirm allow-listed schemes and `://`-containing IRIs still pass |
| `docs/superpowers/specs/2026-07-30-frank-rdf-design.md` | Modify (if present) | Update the error-handling table entry this issue references, if it still describes the old (weaker) guarantee |
| `RELEASE_NOTES.md` | Modify | Note the behavior tightening, mirroring this repo's existing release-notes convention (see recent entries for other packages) |

---

### Task 1: Tighten the fallback and update docs/tests

**Files:** see File Structure above.

**Interfaces:**
- Consumes: existing `resolveIri: prefixes: (string * string) list -> s: string -> string` (`src/Frank.Rdf/Rdf.fs:7-19`).
- Produces: same signature, stricter behavior. No public API shape change — internal function, no caller-visible signature change.

**Exact change** to `resolveIri` in `src/Frank.Rdf/Rdf.fs` (current code shown, then the required replacement):

Current:
```fsharp
let internal resolveIri (prefixes: (string * string) list) (s: string) : string =
    match s.IndexOf ':' with
    | -1 -> failwithf "Frank.Rdf: '%s' is neither an absolute IRI nor a CURIE (no ':')" s
    | i ->
        let prefix = s.Substring(0, i)

        match prefixes |> List.tryFind (fun (p, _) -> p = prefix) with
        | Some(_, ns) -> ns + s.Substring(i + 1)
        | None ->
            if Uri.IsWellFormedUriString(s, UriKind.Absolute) then
                s
            else
                failwithf "Frank.Rdf: undeclared prefix '%s' in '%s'" prefix s
```

Required:
```fsharp
let private nonHierarchicalAbsoluteSchemes = [ "urn:"; "mailto:"; "tel:" ]

let internal resolveIri (prefixes: (string * string) list) (s: string) : string =
    match s.IndexOf ':' with
    | -1 -> failwithf "Frank.Rdf: '%s' is neither an absolute IRI nor a CURIE (no ':')" s
    | i ->
        let prefix = s.Substring(0, i)

        match prefixes |> List.tryFind (fun (p, _) -> p = prefix) with
        | Some(_, ns) -> ns + s.Substring(i + 1)
        | None ->
            let looksAbsolute =
                s.Contains "://"
                || nonHierarchicalAbsoluteSchemes |> List.exists s.StartsWith

            if looksAbsolute && Uri.IsWellFormedUriString(s, UriKind.Absolute) then
                s
            else
                failwithf "Frank.Rdf: undeclared prefix '%s' in '%s'" prefix s
```

Place the new `nonHierarchicalAbsoluteSchemes` binding directly above `resolveIri` (private, module-level — not nested inside the function, so it isn't reallocated per call).

**`Rdf.fsi` doc comment** — update `resolveIri`'s doc comment to state the new guarantee precisely: an undeclared prefix now only passes through when the string contains `://` or starts with an allow-listed scheme (`urn:`, `mailto:`, `tel:`) *and* is well-formed; anything else raises, closing the old "any word:word string looks well-formed" gap.

**Tests to add/modify in `PrefixResolutionTests.fs`:**

- **New, must now raise:** `resolveIri [] "foaf:name"` and `resolveIri [] "schema:Game"` (with no declared prefixes) — both are syntactically "well-formed" under the loose `System.Uri` parser but contain no `://` and match no allow-listed scheme, so they must now raise. (Note: the file's existing test at line 37, `"raises for an undeclared prefix that isn't an absolute IRI either"`, uses `"schema:Game Object"` specifically because it fails well-formedness even under the *old* code — keep that test passing unchanged; add the new cases alongside it rather than replacing it.)
- **Must still pass through unchanged (no regression):** the existing test `"passes an absolute IRI through unchanged when its scheme isn't a declared prefix"` (Wikidata/tictactoe `https://`/`http://` URLs, both contain `://`) must still pass with no changes.
- **New, allow-listed non-hierarchical schemes still pass through:** `resolveIri [] "urn:isbn:0451450523"` and `resolveIri [] "mailto:someone@example.org"` resolve to themselves unchanged.
- **New, still raises for a prefix-only typo using an allow-listed-looking but malformed string:** confirm an allow-listed scheme prefix that is otherwise not well-formed still raises (belt-and-suspenders: the allow-list only loosens the `looksAbsolute` gate, `Uri.IsWellFormedUriString` still runs).

**Verification:** `dotnet test test/Frank.Rdf.Tests/Frank.Rdf.Tests.fsproj` must pass on all three TFMs. Also run the full `Frank.Rdf.Tests` and `Frank.Rdf.Sample`-adjacent suites (`RoundTripTests.fs`, `ToGraphTests.fs`, `MergeTests.fs`) to confirm nothing else relied on the old looser fallback.
