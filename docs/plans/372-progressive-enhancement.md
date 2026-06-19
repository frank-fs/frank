# #372 — Semantic progressive enhancement (decompose, locked 2026-06-17)

Branch: `v7.3.2-rebuild`. Worktree: `.claude/worktrees/v732-rebuild`.
Decisions locked: **new `frank semantic finalize` command**; **exact-only confirm applies to fields too**; **preserve `Excluded` across re-extract**.

Thesis: convention = deterministic LLM-free baseline tier; the zero-LLM floor must be a *graceful baseline* (build passes, thin discovery), not a *cliff* (all-Confirmed gate errors). Achieved by: `Excluded` status + convention-confirms-only-exact + a `finalize` decide step. Investment (LLM clarify→accept, explicit CE) monotonically enriches; never re-paid at build.

Invariant introduced: **no false assertions.** Convention auto-confirms only exact normalized-token identity, for BOTH type→class and field→property. Fuzzy → `Proposed` (suggestion, not assertion).

Committed lock must be **all-decided** (`Confirmed`|`Excluded`). `Proposed`/`Unresolved` are transient draft states; committing them fails the build.

---

## Task 1 — `MappingStatus.Excluded` + serialize/parse

**Files:** `src/Frank.Semantic/Mapping.fs`, `src/Frank.Semantic/LockFile.fs`

`Mapping.fs:28-31` after:
```fsharp
type MappingStatus =
    | Confirmed
    | Proposed
    | Unresolved
    | Excluded
```

`LockFile.fs:31-35` after:
```fsharp
let private statusToString =
    Map.ofList [ Confirmed, "confirmed"; Proposed, "proposed"; Unresolved, "unresolved"; Excluded, "excluded" ]

let private stringToStatus =
    Map.ofList [ "confirmed", Confirmed; "proposed", Proposed; "unresolved", Unresolved; "excluded", Excluded ]
```

**Checkpoint:** `mappingStatusFromString "excluded" = Ok Excluded`; `mappingStatusToString Excluded = "excluded"`. Roundtrip test in `LockFileTests.fs`.
**Scope lock:** only these two files.

## Task 2 — `StatusCounts.Excluded` + `countByStatus` arm + `Clarify` arm

**Files:** `src/Frank.Semantic/LockFile.fs`, `src/Frank.Cli.Core/Clarify.fs`

`LockFile.fs:351-372` after:
```fsharp
type StatusCounts =
    { Confirmed: int
      Proposed: int
      Unresolved: int
      Excluded: int }

let countByStatus (mappings: Mapping list) : StatusCounts =
    let tally (acc: StatusCounts) (m: Mapping) =
        match m.Status with
        | Confirmed -> { acc with Confirmed = acc.Confirmed + 1 }
        | Proposed -> { acc with Proposed = acc.Proposed + 1 }
        | Unresolved -> { acc with Unresolved = acc.Unresolved + 1 }
        | Excluded -> { acc with Excluded = acc.Excluded + 1 }

    List.fold tally { Confirmed = 0; Proposed = 0; Unresolved = 0; Excluded = 0 } mappings
```

`Clarify.fs:64-67` after (Excluded is decided → skip, like Confirmed):
```fsharp
            match m.Status with
            | Unresolved -> (m :: unresolved, proposed)
            | Proposed -> (unresolved, m :: proposed)
            | Confirmed
            | Excluded -> (unresolved, proposed))
```

**Checkpoint:** `dotnet build Frank.sln` green (Pipeline.fs `ExtractSummary = StatusCounts` still compiles — `summarize` unaffected; `printSummary` in Program.fs reads only Confirmed/Proposed/Unresolved fields — verify it still compiles).
**Scope lock:** only these two files. Note: `Pipeline.summarize` / `Program.printSummary` consume `StatusCounts` — do NOT change their output format here (Status.format is Task 8).

## Task 3 — ConventionEngine: confirm only on exact normalized-token identity (type AND field)

**Files:** `src/Frank.Semantic/ConventionEngine.fs`

Add helper near `normalizeTokens` (after line 131):
```fsharp
/// Normalized-token key with no separator (for exact-identity comparison).
let private normKey (name: string) : string = normalizeTokens name |> String.concat ""
```

Make `topK` generic on payload (`ConventionEngine.fs:201-202`):
```fsharp
let private topK (candidates: (float * 'a) list) : (float * 'a) list =
    candidates |> List.sortByDescending fst |> List.truncate topKCandidatesBound
```

`buildFieldMapping` (`:259-286`) — confirm only when the field's normalized key exactly equals the best property key:
```fsharp
    let private buildFieldMapping (properties: Map<string, string>) (field: FieldInfo) : FieldMapping =
        if properties.IsEmpty then
            { Name = field.Name; Iri = None; Confidence = 0.0; Source = Convention; Status = Unresolved }
        else
            let name = bestFieldName field
            let fieldKey = normKey (field.Attributes |> Map.tryFind "JsonPropertyName" |> Option.defaultValue field.Name)

            let bestK, bestIri, conf =
                properties
                |> Map.toSeq
                |> Seq.map (fun (k, iri) -> k, iri, jaroWinkler name k)
                |> Seq.maxBy (fun (_, _, c) -> c)

            let status =
                if fieldKey = bestK then Confirmed
                elif conf > 0.0 then Proposed
                else Unresolved

            { Name = field.Name; Iri = Some bestIri; Confidence = conf; Source = Convention; Status = status }
```

`score` candidate selection (`:353-381`) — carry localName; confirm only on exact:
```fsharp
                let candidates =
                    inScopeClasses
                    |> Map.toList
                    |> List.choose (fun (localName, classIri) ->
                        if hasTokenHit typeTokens localName then
                            Some(combinedScore typeTokens typeInfo.Fields terms.Properties localName, (localName, classIri))
                        else
                            None)
                    |> topK

                match candidates with
                | [] -> emptyUnresolved
                | (bestScore, (bestLocal, bestIri)) :: rest ->
                    let alternates = rest |> List.map (snd >> snd)
                    let fieldMappings = typeInfo.Fields |> List.map (buildFieldMapping terms.Properties)

                    let status =
                        if (typeTokens |> String.concat "") = bestLocal then Confirmed
                        else Proposed

                    { FSharpType = typeInfo.FullName
                      Iri = Some bestIri
                      Confidence = bestScore
                      Source = Convention
                      Status = status
                      Alternates = alternates
                      Fields = fieldMappings }
```

Rationale: rule can only *demote* (≥0.85 fuzzy that used to Confirm now Proposes). Never creates a false confirm. `applyExplicitClass` (Manual/Confirmed) unaffected.

**Checkpoint:** new tests in `ConventionEngineTests.fs`:
- `Player` vs class `play` → `Proposed` (was Confirmed @≥0.85). No `schema:Play` asserted as Confirmed.
- field `Total` vs property `totalpaymentdue` → `Proposed` (was Confirmed @0.867).
- existing AT1 (`Order`↔`order`, exact) → still `Confirmed` (must stay green — verify).
**Scope lock:** only `ConventionEngine.fs`.
**Anti-shortcut:** do NOT lower `confirmationThreshold` or touch `hasTokenHit` (still gates candidate viability at 0.85). The change is the *confirm decision*, not candidate filtering.

## Task 4 — `ResolvedModel.build` filters Excluded

**Files:** `src/Frank.Semantic/ResolvedModel.fs`

`build` (`:249-264`) — filter before `buildResources`:
```fsharp
    let build (registry: VocabularyRegistry) (lock: LockFile) : Result<ResolvedModel, string> =
        let lockIriPrefixes = lockPrefixes lock.Vocabularies
        let included = lock.Mappings |> List.filter (fun m -> m.Status <> Excluded)

        match buildResources lockIriPrefixes registry included with
        ...
```

Rationale: one filter point covers all 3 emitters (Discovery/LinkedData/SemanticModel all call `ResolvedModel.build`). Excluded type = generate nothing.

**Checkpoint:** test — lock with one `Confirmed` + one `Excluded` (Excluded keeps a candidate iri) → `ResolvedModel.build` returns only the Confirmed resource. Emitter output contains no excluded-type triple/descriptor.
**Scope lock:** only `ResolvedModel.fs`.

## Task 5 — `Finalize.fs` pure transform (NEW, Frank.Cli.Core)

**Files:** NEW `src/Frank.Cli.Core/Finalize.fs`; `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (add `<Compile Include="Finalize.fs"/>` after `Status.fs`, line 16)

```fsharp
module Frank.Cli.Core.Finalize

open Frank.Semantic
open Frank.Semantic.LockFile

type FinalizeSummary =
    { Confirmed: int
      Excluded: int
      AlreadyDecided: int }

let private decideField (f: FieldMapping) : FieldMapping =
    match f.Status with
    | Confirmed
    | Excluded -> f
    | Proposed
    | Unresolved -> { f with Status = Excluded; Source = Manual }

let private decideMapping (m: Mapping) : Mapping =
    let fields = m.Fields |> List.map decideField
    match m.Status with
    | Confirmed
    | Excluded -> { m with Fields = fields }
    | Proposed
    | Unresolved -> { m with Status = Excluded; Source = Manual; Fields = fields }

/// Resolve a draft lock to all-decided: Confirmed stays; everything else Excluded.
/// Deterministic, zero tokens. Pure.
let run (lf: LockFile) : LockFile * FinalizeSummary =
    let decided = lf.Mappings |> List.map decideMapping
    let counts = countByStatus decided

    let alreadyDecided =
        lf.Mappings
        |> List.filter (fun m -> m.Status = Confirmed || m.Status = Excluded)
        |> List.length

    { lf with Mappings = decided },
    { Confirmed = counts.Confirmed
      Excluded = counts.Excluded
      AlreadyDecided = alreadyDecided }
```

**Checkpoint:** `FinalizeTests.fs`:
- draft (1 Confirmed, 1 Proposed, 1 Unresolved) → result all-decided; Proposed+Unresolved → Excluded(Manual); Confirmed unchanged.
- Confirmed mapping with a Proposed field → field becomes Excluded.
- idempotent: `run (fst (run lf)) = run lf` mappings-equal.
**Scope lock:** new file + fsproj line only.

## Task 6 — `frank semantic finalize` command wiring

**Files:** `src/Frank.Cli/Program.fs`

Add `FinalizeArgs` (clone of `StatusArgs`, `:61-71`):
```fsharp
[<CliPrefix(CliPrefix.DoubleDash)>]
type FinalizeArgs =
    | [<AltCommandLine("-p")>] Project of path: string
    | [<AltCommandLine("-l")>] Lock_File of path: string

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Project _ -> "path to the .fsproj (defaults to first .fsproj in current directory)"
            | Lock_File _ -> "path to the lock file (defaults to <projectdir>/.frank/semantic-mappings.lock.json)"
```

`SemanticArgs` (`:85-100`) add case + usage:
```fsharp
    | [<CliPrefix(CliPrefix.None)>] Finalize of ParseResults<FinalizeArgs>
    ...
            | Finalize _ -> "decide a draft lock: confirm exact matches, exclude the rest (zero LLM)"
```

`handleFinalize` (after `handleStatus`, model on it — reads lock, runs Finalize.run, writes, prints):
```fsharp
let private handleFinalize (args: ParseResults<FinalizeArgs>) : int =
    match lockPathFrom (args.TryGetResult FinalizeArgs.Lock_File) (args.TryGetResult FinalizeArgs.Project) with
    | Error e -> eprintfn "error: %s" e; 1
    | Ok lockPath ->
        match read lockPath with
        | Error e -> eprintfn "error: %s" e; 1
        | Ok lf ->
            let updated, summary = Finalize.run lf
            LockFile.write lockPath updated
            printfn "Finalized: %d confirmed, %d excluded (%d already decided)" summary.Confirmed summary.Excluded summary.AlreadyDecided
            0
```

`handleSemantic` (`:345-351`) add arm:
```fsharp
    | SemanticArgs.Finalize finalizeArgs -> handleFinalize finalizeArgs
```

**Checkpoint:** `frank semantic finalize --lock-file <draft>` exits 0, rewrites lock all-decided; `frank semantic status` on result shows Proposed:0 Unresolved:0.
**Scope lock:** only `Program.fs`.

## Task 7 — `ValidateLockFileTask` decided-gate

**Files:** `src/Frank.Cli.MSBuild/ValidateLockFileTask.fs`

`:28-49` after — pass on Confirmed|Excluded; fail on Proposed|Unresolved; skip fields of excluded mappings:
```fsharp
                let isDecided status = status = Confirmed || status = Excluded

                let draftMappings =
                    lock.Mappings |> List.filter (fun m -> not (isDecided m.Status))

                let draftFields =
                    lock.Mappings
                    |> List.filter (fun m -> m.Status <> Excluded)
                    |> List.collect (fun m -> m.Fields)
                    |> List.filter (fun f -> not (isDecided f.Status))

                let total = draftMappings.Length + draftFields.Length

                if total > 0 then
                    this.Log.LogError(
                        subcategory = null, errorCode = "MS001", helpKeyword = null,
                        file = this.LockFilePath, lineNumber = 0, columnNumber = 0,
                        endLineNumber = 0, endColumnNumber = 0,
                        message = $"Lock file has {total} undecided (proposed/unresolved) mapping(s); run 'frank semantic finalize' (zero-LLM) or 'frank semantic clarify' (LLM) to decide.",
                        messageArgs = [||])
                    false
                else
                    true
```

**Checkpoint:** `ValidateLockFileTaskTests.fs`:
- lock of Confirmed+Excluded only → `Execute() = true` (AT5).
- lock with one Proposed → `Execute() = false`, MS001 logged (AT4 — preserves milestone negative test).
- existing all-confirmed test → still true.
**Scope lock:** only `ValidateLockFileTask.fs`.

## Task 8 — `Status.format` + StatusTests

**Files:** `src/Frank.Cli.Core/Status.fs`, `test/Frank.Cli.Core.Tests/StatusTests.fs`

`Status.fs` after:
```fsharp
let format (lf: LockFile) : string =
    let c = countByStatus lf.Mappings
    $"Confirmed:  {c.Confirmed}\nProposed:   {c.Proposed}\nUnresolved: {c.Unresolved}\nExcluded:   {c.Excluded}"
```

StatusTests: add `Excluded` cases to the fixtures + `Expect.stringContains output "Excluded:   N"`. Do NOT weaken existing Confirmed/Proposed/Unresolved assertions.

**Checkpoint:** StatusTests green.
**Scope lock:** these two files.

## Task 9 — Preserve `Excluded` across re-extract

**Files:** `src/Frank.Cli.Core/Pipeline.fs`

`mergeWithPreservation` (`:101`) after:
```fsharp
            let isProtected =
                (m.Status = Confirmed || m.Status = Excluded) && (m.Source = Llm || m.Source = Manual)
```

Rationale: finalize stamps excluded entries `Source = Manual`. Without this, a finalize→extract sequence silently resurrects an excluded type as a fresh convention proposal.

**Checkpoint:** test — existing lock with `Excluded`(Manual) entry; re-run merge with fresh convention proposal for same type → Excluded preserved. Confirmed(Llm/Manual) still preserved (existing behavior unchanged).
**Scope lock:** only `Pipeline.fs`.

## Task 10 — Emitter/LockFile roundtrip tests for Excluded

**Files:** `test/Frank.Semantic.Tests/LockFileTests.fs`, `test/Frank.Cli.Core.Tests/{DiscoveryEmitterTests,LinkedDataEmitterTests,SemanticModelEmitterTests}.fs`

- LockFile: Excluded mapping serializes `"status":"excluded"` and roundtrips.
- Each emitter: a lock with a Confirmed + an Excluded mapping → output references only the Confirmed (no excluded iri/local-name string present).

**Checkpoint:** all three emitter test suites + LockFileTests green.
**Scope lock:** test files only.

## Task 11 — E2E AT1–AT5 (TicTacToe sample)

**Files:** TBD — `find sample/ -name test-e2e.sh`; the TicTacToe semantic sample + its build/discovery harness.

- AT1 floor: `using "schema"` mixed names → extract → `finalize` → committed lock all-decided → `dotnet build` succeeds; discovery surface = exactly the exact-matched types.
- AT2: `Player` excluded, not confirmed; no `Player→schema:Play` triple in any artifact.
- AT3 monotonic: LLM clarify→accept confirming `Move→schema:MoveAction` → rebuild → discovery adds `Move`; strict superset of AT1; nothing lost.
- AT4: lock with any Proposed/Unresolved → `dotnet build` fails (MS001).
- AT5: Confirmed+Excluded lock → build succeeds; excluded absent from every artifact.

**Checkpoint:** scripted E2E passes; verify build exit codes + grep generated artifacts directly.
**Scope lock:** sample + e2e harness. (Investigate exact files at execution start — sample shape not yet read.)

## Task 12 — Full verification

`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln` + `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` + the separately-built test projects (`Frank.Semantic.Tests`, `Frank.Cli.Core.Tests`, `Frank.Cli.MSBuild.Tests` — NOT in Frank.sln) + `dotnet fantomas --check src/`.

---

## Shortcut audit
- **Convention demote-only:** new rule can never create a false confirm (only demote ≥0.85 fuzzy to Proposed). Verified against AT1 (exact `Order`↔`order` stays Confirmed).
- **Single filter point:** Excluded filtered once in `ResolvedModel.build` — no per-emitter drift.
- **Finalize uniform rule:** non-{Confirmed,Excluded} → Excluded for both mappings and fields; idempotent; deterministic; zero tokens.
- **Gate field-skip:** excluded mappings' fields ignored (excluded type generates nothing, fields irrelevant) — prevents a confirmed-but-fielded-draft false pass.
- **Excluded persistence:** Source=Manual stamp + protection extension closes the finalize→extract footgun.
- **Anti-shortcut (Task 3):** must NOT lower the threshold or alter candidate filtering; only the confirm decision changes.
- **#370 NOT in scope:** `vocabularies`-populate + integrity checksum are #370. Excluded changes here stabilize the schema that #370's checksum will later cover.
