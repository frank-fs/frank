# DU-Aware Convention Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make convention extraction treat a discriminated union as a *sum* — emit its structure mechanically and join case/payload name-tokens against the entities the loaded vocabulary declares (classes, properties, individuals), instead of fuzzy-matching case names as if they were record properties.

**Architecture:** Three layers — structural (certain, from FCS), lexical (conservative exact/fuzzy join), domain-investment (residue). `TypeInfo` and `Mapping` each gain a sum-aware `Shape = Record | Union` (no flattening of the type→cases→payload tree). The convention engine routes a nullary case to vocabulary *individuals* and a payload-carrying case to *subclasses*, confirming only on whole-`normKey` exact identity (the existing rule). The generated anti-drift model emits a per-union match function over the real F# constructors so a case rename breaks the build.

**Tech Stack:** F# (net8.0/9.0/10.0 multi-target), FSharp.Compiler.Service (FCS), dotnetRDF (VDS.RDF), Expecto + FsCheck tests, System.Text.Json.

**Environment:** All commands need `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (nix-darwin ICU). `Frank.Semantic.Tests`, `Frank.Cli.Core.Tests`, `Frank.Cli.MSBuild.Tests` are NOT in `Frank.sln` — run each project directly. Work in the worktree `/Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild` (branch `v7.3.2-rebuild`). After ANY lock change, `rm -rf obj bin` before rebuild (stale incremental codegen trap).

**Scope note:** This is one cohesive vertical but a large one. Tasks 3 and 4 are *atomic type-shape migrations* — the shared record changes plus all direct consumers move together in a single commit (F# will not compile a half-migrated record). They are larger than the 2–5 minute ideal; that is unavoidable for shared F# record types. Every task still ends green and committed.

---

## File Structure

**Modified (shared types):**
- `src/Frank.Semantic/Mapping.fs` — add `TypeShape`/`CaseInfo` (Task 3) and `MappingShape`/`CaseMapping` (Task 4); replace flat `Fields` with `Shape` on both `TypeInfo` and `Mapping`.

**Modified (producers/consumers):**
- `src/Frank.Cli.Core/Extractor.fs` — produce sum-aware `TypeInfo` (Task 3).
- `src/Frank.Semantic/ConventionEngine.fs` — multipart tokenizer (Task 1); `Individuals` in `VocabTerms` (Task 2); union join (Task 5).
- `src/Frank.Semantic/LockFile.fs` — serialize/parse/merge/count the `Shape` (Task 4).
- `src/Frank.Semantic/ResolvedModel.fs` — Excluded-filter cases as well as fields (Task 4).
- `src/Frank.Cli.Core/Finalize.fs` — decide cases + payloads (Task 4).
- `src/Frank.Cli.MSBuild/ValidateLockFileTask.fs` — gate counts undecided cases + payloads (Task 4).
- `src/Frank.Cli.Core/SemanticModelEmitter.fs` — per-union case match function (Task 6).

**Tests:**
- `test/Frank.Semantic.Tests/ConventionEngineTests.fs` — tokenizer (Task 1), individuals (Task 2), union join (Task 5).
- `test/Frank.Cli.Core.Tests/ExtractorTests.fs` — sum-aware extraction (Task 3).
- `test/Frank.Semantic.Tests/LockFileTests.fs` — shape round-trip (Task 4).
- `test/Frank.Semantic.Tests/ResolvedModelTests.fs` — case Excluded-filter (Task 4).
- `test/Frank.Cli.Core.Tests/FinalizeTests.fs` — case decide (Task 4).
- `test/Frank.Cli.MSBuild.Tests/ValidateLockFileTaskTests.fs` — gate on cases (Task 4).
- `test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs` — case match emission (Task 6).
- `sample/TicTacToe-v732/test-floor-e2e.sh` — anti-drift + sample re-bless (Task 7).

---

## Task 1: Multipart tokenizer (single-capital prefix + acronym run)

Independent, pure, isolated. Goes first.

**Files:**
- Modify: `src/Frank.Semantic/ConventionEngine.fs:110-120` (`splitPascalCase`)
- Test: `test/Frank.Semantic.Tests/ConventionEngineTests.fs` (the `normalizationTests` list, ~line 59)

- [ ] **Step 1: Write failing tests**

Add these tests inside the existing `normalizationTests` `testList` in `test/Frank.Semantic.Tests/ConventionEngineTests.fs` (append to the list before its closing `]`):

```fsharp
          test "single-capital prefix is its own token" {
              let tokens = ConventionEngine.normalizeTokens "XMove"
              Expect.equal tokens [ "x"; "move" ] "XMove → x, move"
          }

          test "multipart payload type splits" {
              let tokens = ConventionEngine.normalizeTokens "SquarePosition"
              Expect.equal tokens [ "square"; "position" ] "SquarePosition → square, position"
          }

          test "acronym run then word splits before trailing word" {
              let tokens = ConventionEngine.normalizeTokens "HTTPSConfig"
              Expect.equal tokens [ "https"; "config" ] "HTTPSConfig → https, config"
          }

          test "trailing single capital stays attached" {
              let tokens = ConventionEngine.normalizeTokens "PointX"
              Expect.equal tokens [ "point"; "x" ] "PointX → point, x"
          }
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ --filter "Name normalization" -v quiet`
Expected: FAIL. `XMove` currently yields `[ "xmove" ]` (no lower→upper boundary), `SquarePosition` already passes, `HTTPSConfig` yields `[ "httpsconfig" ]`.

- [ ] **Step 3: Replace `splitPascalCase`**

The current splitter only cuts at a lower→upper boundary. Generalize to also cut at an upper-run→capitalized-word boundary (acronym handling) while keeping a trailing acronym/single-capital as its own token. Replace `splitPascalCase` (`ConventionEngine.fs:110-120`) with:

```fsharp
    let private splitPascalCase (name: string) : string list =
        let mutable tokens = []
        let mutable start = 0

        for i in 1 .. name.Length - 1 do
            let prev = name.[i - 1]
            let cur = name.[i]
            // boundary A: lower/digit → upper   (squareP|osition, point|X)
            let lowerToUpper = Char.IsUpper cur && not (Char.IsUpper prev)
            // boundary B: end of an acronym run → start of a capitalized word
            // (HTTPS|Config): cur is upper, prev is upper, and the char AFTER cur is lower
            let acronymToWord =
                Char.IsUpper cur
                && Char.IsUpper prev
                && i + 1 < name.Length
                && Char.IsLower name.[i + 1]

            if lowerToUpper || acronymToWord then
                tokens <- name.[start .. i - 1].ToLowerInvariant() :: tokens
                start <- i

        tokens <- name.[start .. name.Length - 1].ToLowerInvariant() :: tokens
        List.rev tokens
```

- [ ] **Step 4: Run tests, verify pass**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ --filter "Name normalization" -v quiet`
Expected: PASS. Verify existing cases still hold: `URL → [url]`, `CustomerOrder → [customer; order]`, `Order → [order]`.

- [ ] **Step 5: Commit**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
git add src/Frank.Semantic/ConventionEngine.fs test/Frank.Semantic.Tests/ConventionEngineTests.fs
git commit -m "feat(semantic): multipart tokenizer — single-capital prefix + acronym split"
```

---

## Task 2: VocabTerms gains `Individuals`

Additive field on `VocabTerms`; populated in `extractVocabTerms`. Records/score behaviour unchanged.

**Files:**
- Modify: `src/Frank.Semantic/ConventionEngine.fs:7-10` (`VocabTerms` type), `:217-221` (type-IRI constants), `:233-259` (`collectByTypeIri`, `extractVocabTerms`)
- Test: `test/Frank.Semantic.Tests/ConventionEngineTests.fs`

- [ ] **Step 1: Write failing test**

Add a new `testList` to `test/Frank.Semantic.Tests/ConventionEngineTests.fs`. It parses a tiny JSON-LD vocab declaring two individuals and asserts they surface in `VocabTerms.Individuals`. Match the existing graph-building style in that file (it already uses `VocabFetcher.parseGraph`); if a helper `parseTerms` exists reuse it, otherwise inline:

```fsharp
[<Tests>]
let individualExtractionTests =
    testList
        "VocabTerms.Individuals"
        [ test "owl:NamedIndividual surfaces as an individual" {
              let jsonld =
                  """
                  { "@context": { "owl": "http://www.w3.org/2002/07/owl#" },
                    "@graph": [
                      { "@id": "https://ex.org/X", "@type": "owl:NamedIndividual" },
                      { "@id": "https://ex.org/O", "@type": "owl:NamedIndividual" } ] }
                  """

              let graph =
                  VocabFetcher.parseGraph VocabFetcher.JsonLd (System.Text.Encoding.UTF8.GetBytes jsonld)
                  |> function
                      | Ok g -> g
                      | Error e -> failwith e

              let terms = ConventionEngine.extractVocabTerms graph
              Expect.isTrue (terms.Individuals.ContainsKey "x") "x individual present"
              Expect.equal terms.Individuals.["x"] "https://ex.org/X" "x IRI"
              Expect.isTrue (terms.Individuals.ContainsKey "o") "o individual present"
          } ]
```

Confirm the open statements at the top of the test file include `open Frank.Semantic`. If `VocabFetcher` is not already opened/visible, qualify as `Frank.Semantic.VocabFetcher`.

- [ ] **Step 2: Run test, verify fail**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ --filter "VocabTerms.Individuals" -v quiet`
Expected: FAIL to compile — `VocabTerms` has no `Individuals` field.

- [ ] **Step 3: Add `Individuals` to the type**

Replace `VocabTerms` (`ConventionEngine.fs:7-10`):

```fsharp
/// Extracted class/property/individual local names from a vocabulary IGraph.
/// Keys are lowercase local names; values are absolute IRI strings.
type VocabTerms =
    { Classes: Map<string, string>
      Properties: Map<string, string>
      Individuals: Map<string, string> }
```

- [ ] **Step 4: Add type-IRI constants**

After the existing constants block (`ConventionEngine.fs:217-221`), add:

```fsharp
    let private owlNamedIndividualIri = "http://www.w3.org/2002/07/owl#NamedIndividual"
    let private skosConceptIri = "http://www.w3.org/2004/02/skos/core#Concept"
```

- [ ] **Step 5: Populate in `extractVocabTerms`**

Replace `extractVocabTerms` (`ConventionEngine.fs:249-259`):

```fsharp
    /// Extract class, property, and individual local names from a vocabulary IGraph.
    /// Recognizes rdfs:Class, rdf:Property, schema:Class, schema:Property typings,
    /// plus owl:NamedIndividual and skos:Concept as individuals (enumerated values).
    /// Keys are lowercase local names; values are absolute IRI strings.
    let extractVocabTerms (graph: IGraph) : VocabTerms =
        let rdfsClasses = collectByTypeIri rdfsClassIri graph
        let schemaClasses = collectByTypeIri schemaClassIri graph
        let rdfProperties = collectByTypeIri rdfPropertyIri graph
        let schemaProperties = collectByTypeIri schemaPropertyIri graph
        let owlIndividuals = collectByTypeIri owlNamedIndividualIri graph
        let skosConcepts = collectByTypeIri skosConceptIri graph

        { Classes = mergeTermMaps rdfsClasses schemaClasses
          Properties = mergeTermMaps rdfProperties schemaProperties
          Individuals = mergeTermMaps owlIndividuals skosConcepts }
```

- [ ] **Step 6: Fix every other `VocabTerms` construction site**

Search and update each literal that constructs a `VocabTerms`:

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; grep -rn "Classes =" --include=*.fs src test | grep -i "Properties ="`

For each `{ Classes = ...; Properties = ... }` literal (in `ConventionEngine.fs` `emptyTerms` if present, and in test fixtures across `test/Frank.Semantic.Tests/`), add `Individuals = Map.empty` (or the relevant map). There is at least one in-engine empty-terms value and several test fixtures. Update each so the record is complete.

- [ ] **Step 7: Run tests, verify pass**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ -v quiet`
Expected: PASS — new individuals test green, all existing ConventionEngine tests still green.

- [ ] **Step 8: Commit**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
git add src/Frank.Semantic/ConventionEngine.fs test/Frank.Semantic.Tests/
git commit -m "feat(semantic): extract vocabulary individuals (owl:NamedIndividual, skos:Concept)"
```

---

## Task 3: Sum-aware `TypeInfo` + Extractor (ATOMIC migration)

Replace flat `TypeInfo.Fields` with `Shape = Record | Union`. Records behave identically; unions now carry cases + payload. Convention engine consumes the new shape (records unchanged; unions get the type-class match only — case mapping arrives in Task 5).

**Files:**
- Modify: `src/Frank.Semantic/Mapping.fs:1-19` (add `CaseInfo`, `TypeShape`; change `TypeInfo`)
- Modify: `src/Frank.Cli.Core/Extractor.fs:70-106` (drop `unionCaseToFieldInfo`; add case + payload extraction; `entityToTypeInfo`)
- Modify: `src/Frank.Semantic/ConventionEngine.fs` (`score` `:347-399`, `applyExplicitClass` `:318-341`, `combinedScore`) — read `Shape`
- Test: `test/Frank.Cli.Core.Tests/ExtractorTests.fs`

- [ ] **Step 1: Write failing tests**

The existing DU tests in `ExtractorTests.fs` (around lines 112–152) assert the old flat-`Fields` shape and must be **rewritten** to the new shape (do not delete — refactor to cover the surviving behaviour). Replace the `at2DuTests` list with:

```fsharp
let duSource =
    """
namespace MyApp

type Status =
    | Pending
    | Shipped of trackingNumber: string

type Move =
    | XMove of position: SquarePosition
    | OMove of SquarePosition

and SquarePosition = { Row: int; Col: int }
"""

[<Tests>]
let at2DuTests =
    testList
        "AT2 - DU extraction (sum-aware)"
        [ test "Status is a Union shape" {
              let types =
                  Expect.wantOk (Extractor.extractTypeInfosFromSource duSource) "extract"

              let status = types |> List.find (fun t -> t.LocalName = "Status")

              match status.Shape with
              | Union cases ->
                  let names = cases |> List.map (fun c -> c.Name)
                  Expect.contains names "Pending" "Pending case"
                  Expect.contains names "Shipped" "Shipped case"
              | Record _ -> failwith "Status should be a Union"
          }

          test "nullary case has empty payload" {
              let types =
                  Expect.wantOk (Extractor.extractTypeInfosFromSource duSource) "extract"

              let status = types |> List.find (fun t -> t.LocalName = "Status")

              match status.Shape with
              | Union cases ->
                  let pending = cases |> List.find (fun c -> c.Name = "Pending")
                  Expect.isEmpty pending.Payload "Pending has no payload"
              | Record _ -> failwith "Status should be a Union"
          }

          test "labeled payload uses the label as field name" {
              let types =
                  Expect.wantOk (Extractor.extractTypeInfosFromSource duSource) "extract"

              let status = types |> List.find (fun t -> t.LocalName = "Status")

              match status.Shape with
              | Union cases ->
                  let shipped = cases |> List.find (fun c -> c.Name = "Shipped")
                  let f = shipped.Payload |> List.exactlyOne
                  Expect.equal f.Name "trackingNumber" "label is the field name"
                  Expect.stringContains f.TypeName "string" "payload type"
              | Record _ -> failwith "Status should be a Union"
          }

          test "record type is a Record shape" {
              let types =
                  Expect.wantOk (Extractor.extractTypeInfosFromSource duSource) "extract"

              let sq = types |> List.find (fun t -> t.LocalName = "SquarePosition")

              match sq.Shape with
              | Record fields ->
                  let names = fields |> List.map (fun f -> f.Name)
                  Expect.contains names "Row" "Row field"
                  Expect.contains names "Col" "Col field"
              | Union _ -> failwith "SquarePosition should be a Record"
          } ]
```

Also: any other test in the repo that reads `.Fields` on a `TypeInfo` must be migrated. Find them in Step 5.

- [ ] **Step 2: Run tests, verify fail**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "AT2" -v quiet`
Expected: FAIL to compile — `TypeInfo` has no `Shape`, `CaseInfo` undefined.

- [ ] **Step 3: Change the shared types**

Replace `TypeInfo` and insert `CaseInfo`/`TypeShape` in `src/Frank.Semantic/Mapping.fs` (replace lines 1-19, keeping `FieldInfo` as-is):

```fsharp
namespace Frank.Semantic

/// Input type: FCS-extracted field/payload metadata. Populated by Frank.Cli.Core.
type FieldInfo =
    { Name: string
      TypeName: string
      Attributes: Map<string, string>
      DocComment: string option }

/// One case of a discriminated union. Payload is [] for a nullary case.
type CaseInfo =
    { Name: string
      Payload: FieldInfo list
      Attributes: Map<string, string>
      DocComment: string option }

/// A type is either a product (record) or a sum (union). Preserves the
/// type → cases → payload tree; never flattened into one field list.
type TypeShape =
    | Record of FieldInfo list
    | Union of CaseInfo list

/// Input type: FCS-extracted type metadata. Populated by Frank.Cli.Core.
type TypeInfo =
    { FullName: string
      Namespace: string
      LocalName: string
      Shape: TypeShape
      Attributes: Map<string, string>
      DocComment: string option }
```

- [ ] **Step 4: Rewrite the extractor**

In `src/Frank.Cli.Core/Extractor.fs`, replace `unionCaseToFieldInfo` (lines 70-82) with payload-aware case extraction, then update `entityToTypeInfo` (lines 86-106).

Replace `unionCaseToFieldInfo` with:

```fsharp
    // Payload field name priority: explicit label > nothing-from-the-name.
    // FCS gives generated names "Item", "Item1", ... for unlabeled payloads; we
    // keep the FCS field name only when the author supplied a label, otherwise we
    // fall back to the payload TYPE name so type-name tokens can drive the join.
    let private payloadFieldInfo (ucField: FSharpField) : FieldInfo =
        let typeName =
            ucField.FieldType.Format FSharpDisplayContext.Empty |> normalizeTypeName

        let isGenerated =
            ucField.Name = "Item"
            || (ucField.Name.StartsWith("Item", StringComparison.Ordinal)
                && ucField.Name.Length > 4
                && ucField.Name.[4..] |> Seq.forall Char.IsDigit)

        let name = if isGenerated then typeName else ucField.Name

        { Name = name
          TypeName = typeName
          Attributes = buildAttributeMap (Seq.append ucField.FieldAttributes ucField.PropertyAttributes)
          DocComment = docCommentOf ucField.XmlDoc }

    let private unionCaseToCaseInfo (uc: FSharpUnionCase) : CaseInfo =
        let payload =
            if uc.HasFields then
                uc.Fields |> Seq.map payloadFieldInfo |> Seq.toList
            else
                []

        { Name = uc.Name
          Payload = payload
          Attributes = buildAttributeMap uc.Attributes
          DocComment = docCommentOf uc.XmlDoc }
```

Replace `entityToTypeInfo` (lines 86-106) with:

```fsharp
    let private entityToTypeInfo (entity: FSharpEntity) : TypeInfo option =
        if entity.IsNamespace || entity.IsFSharpModule then
            None
        else
            let ns = entity.Namespace |> Option.defaultValue ""

            let shape =
                if entity.IsFSharpUnion then
                    entity.UnionCases |> Seq.map unionCaseToCaseInfo |> Seq.toList |> Union
                else
                    entity.FSharpFields |> Seq.map fieldToFieldInfo |> Seq.toList |> Record

            Some
                { FullName = entity.TryFullName |> Option.defaultValue entity.LogicalName
                  Namespace = ns
                  LocalName = entity.LogicalName
                  Shape = shape
                  Attributes = buildAttributeMap entity.Attributes
                  DocComment = docCommentOf entity.XmlDoc }
```

Note: a non-record, non-union entity (e.g. a class) now becomes `Record []` rather than being carried with empty fields. This matches the prior behaviour (empty field list) and the convention engine treats `Record []` exactly as the old empty-`Fields` case.

- [ ] **Step 5: Update ConventionEngine to read `Shape`**

`score` and `applyExplicitClass` reference `typeInfo.Fields`. Introduce a helper that yields the record fields for the field-mapping path, and treat a union's fields as empty here (union case mapping is Task 5; for now a union maps only its type-class). Add near the top of the `ConventionEngine` module (after `normKey`):

```fsharp
    /// Record fields used for the field-mapping path. A union contributes no
    /// record fields here (its cases are mapped separately in `score`).
    let private recordFields (typeInfo: TypeInfo) : FieldInfo list =
        match typeInfo.Shape with
        | Record fields -> fields
        | Union _ -> []
```

In `score` (lines 347-399), replace the three uses of `typeInfo.Fields`:
- `combinedScore typeTokens typeInfo.Fields terms.Properties localName` → `combinedScore typeTokens (recordFields typeInfo) terms.Properties localName`
- `let fieldMappings = typeInfo.Fields |> List.map (buildFieldMapping ...)` → `let fieldMappings = recordFields typeInfo |> List.map (buildFieldMapping ...)`

In `applyExplicitClass` (lines 318-341), replace both `typeInfo.Fields` references:
- `if convention.Fields.IsEmpty && not typeInfo.Fields.IsEmpty then` → `if convention.Fields.IsEmpty && not (recordFields typeInfo).IsEmpty then`
- `typeInfo.Fields |> List.map (buildFieldMapping ...)` → `recordFields typeInfo |> List.map (buildFieldMapping ...)`

(`Mapping.Fields` is unchanged in this task — that migration is Task 4. `score` still emits a `Mapping` with `Fields`.)

- [ ] **Step 6: Migrate remaining `.Fields`/`TypeInfo` literal sites**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; grep -rn "LocalName =" --include=*.fs src test | grep -v "//"`
For each `TypeInfo` literal (chiefly in `test/Frank.Semantic.Tests/ConventionEngineTests.fs` fixtures), replace `Fields = [ ... ]` with `Shape = Record [ ... ]` (or `Shape = Union [ ... ]` where a union is intended). Run also: `grep -rn "\.Fields" --include=*.fs test src | grep -i typeinfo` and any `typeInfo.Fields` read → route through `recordFields` or pattern-match `Shape`.

- [ ] **Step 7: Build + test green**

Run:
```
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build src/Frank.Semantic/ src/Frank.Cli.Core/
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ -v quiet
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ -v quiet
```
Expected: PASS. Record extraction and all convention scoring behave as before; DU extraction now sum-aware.

- [ ] **Step 8: Commit**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
git add src/Frank.Semantic/Mapping.fs src/Frank.Cli.Core/Extractor.fs src/Frank.Semantic/ConventionEngine.fs test/
git commit -m "feat(semantic): sum-aware TypeInfo (Record|Union); extractor preserves DU tree"
```

---

## Task 4: Sum-aware `Mapping` + lock/resolve/finalize/gate (ATOMIC migration)

Replace flat `Mapping.Fields` with `Shape = Record of FieldMapping list | Union of CaseMapping list`. Records serialize as before; unions are not yet *populated* with real case mappings (that is Task 5) — for now `score` emits `Record fields` for records and `Union []` for unions, keeping every layer green.

**Files:**
- Modify: `src/Frank.Semantic/Mapping.fs` (add `CaseMapping`, `MappingShape`; change `Mapping`)
- Modify: `src/Frank.Semantic/LockFile.fs` (serialize/parse/merge/countByStatus)
- Modify: `src/Frank.Semantic/ResolvedModel.fs` (`buildResource` Excluded filter)
- Modify: `src/Frank.Cli.Core/Finalize.fs` (`decideMapping`)
- Modify: `src/Frank.Cli.MSBuild/ValidateLockFileTask.fs` (gate)
- Modify: `src/Frank.Semantic/ConventionEngine.fs` (`score`/`applyExplicitClass` emit `Shape`)
- Tests: `LockFileTests.fs`, `ResolvedModelTests.fs`, `FinalizeTests.fs`, `ValidateLockFileTaskTests.fs`, `ConventionEngineTests.fs`

- [ ] **Step 1: Write failing lock round-trip test**

Add to `test/Frank.Semantic.Tests/LockFileTests.fs`:

```fsharp
[<Tests>]
let unionShapeRoundTripTests =
    testList
        "Union shape round-trip"
        [ test "a union mapping serializes and parses back with cases" {
              let mapping =
                  { FSharpType = "MyApp.Move"
                    Iri = Some "ex:Move"
                    Confidence = 1.0
                    Source = Convention
                    Status = Confirmed
                    Alternates = []
                    Shape =
                      Union
                          [ { Name = "XMove"
                              Iri = Some "ex:XMove"
                              Confidence = 1.0
                              Source = Convention
                              Status = Confirmed
                              Payload =
                                [ { Name = "position"
                                    Iri = Some "ex:position"
                                    Confidence = 1.0
                                    Source = Convention
                                    Status = Confirmed } ] } ] }

              let lf =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.Parse "2026-01-01T00:00:00Z"
                    Vocabularies = Map.empty
                    Mappings = [ mapping ] }

              let path = System.IO.Path.GetTempFileName()
              LockFile.write path lf
              let parsed = Expect.wantOk (LockFile.read path) "read back"
              Expect.equal parsed.Mappings lf.Mappings "round-trips with union shape"
          } ]
```

- [ ] **Step 2: Run, verify fail**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ --filter "Union shape round-trip" -v quiet`
Expected: FAIL to compile — `Mapping` has no `Shape`, `CaseMapping`/`Union` undefined.

- [ ] **Step 3: Change the shared mapping types**

In `src/Frank.Semantic/Mapping.fs`, keep `FieldMapping` as-is; replace the `Mapping` record (current lines 43-52) with `CaseMapping` + `MappingShape` + a new `Mapping`:

```fsharp
/// Resolved mapping for one union case. Payload is [] for a nullary case;
/// for a payload-carrying case it holds the case's field mappings.
type CaseMapping =
    { Name: string
      Iri: string option
      Confidence: float
      Source: MappingSource
      Status: MappingStatus
      Payload: FieldMapping list }

/// A mapped type is a product (record fields) or a sum (union cases).
type MappingShape =
    | Record of FieldMapping list
    | Union of CaseMapping list

/// Candidate mapping produced by the convention engine for one TypeInfo.
type Mapping =
    { FSharpType: string
      Iri: string option
      Confidence: float
      Source: MappingSource
      Status: MappingStatus
      Alternates: string list
      Shape: MappingShape }
```

Note: `MappingShape.Record`/`Union` are distinct from `TypeShape.Record`/`Union` (different types, same case names — F# resolves by expected type; qualify as `MappingShape.Record` if an ambiguity warning appears).

- [ ] **Step 4: Add a `Shape` field-flatten helper to Mapping module**

Several consumers (gate, finalize, resolve) need "all field mappings regardless of shape" and "map over every field/case preserving structure." Add these pure helpers to `Mapping.fs` after the `Mapping` type (they belong with the type, are reused by 3 modules — avoids duplication):

```fsharp
[<RequireQualifiedAccess>]
module MappingShape =

    /// All leaf field mappings in a shape (record fields, or every case's payload).
    /// Case mappings themselves are NOT field mappings — see `caseMappings`.
    let payloadFields (shape: MappingShape) : FieldMapping list =
        match shape with
        | Record fs -> fs
        | Union cases -> cases |> List.collect (fun c -> c.Payload)

    let caseMappings (shape: MappingShape) : CaseMapping list =
        match shape with
        | Record _ -> []
        | Union cases -> cases

    /// Map every field mapping (record field or case payload field) through f,
    /// preserving the tree.
    let mapFields (f: FieldMapping -> FieldMapping) (shape: MappingShape) : MappingShape =
        match shape with
        | Record fs -> Record(List.map f fs)
        | Union cases -> Union(cases |> List.map (fun c -> { c with Payload = List.map f c.Payload }))
```

- [ ] **Step 5: Update `ConventionEngine.score` / `applyExplicitClass` to emit `Shape`**

Wherever `score`/`applyExplicitClass`/`emptyUnresolved` build a `Mapping` with `Fields = ...`, change to `Shape = ...`:
- `emptyUnresolved`: `Fields = []` → `Shape = MappingShape.Record []`
- the convention success branch: `Fields = fieldMappings` → `Shape = MappingShape.Record fieldMappings` (records); for a `Union` typeInfo, emit `Shape = MappingShape.Union []` for now. Compute via:

```fsharp
                    let shape =
                        match typeInfo.Shape with
                        | TypeShape.Union _ -> MappingShape.Union []
                        | TypeShape.Record _ -> MappingShape.Record fieldMappings
```
  and set `Shape = shape`.
- `applyExplicitClass`: it reads `convention.Fields`. Replace with `MappingShape.payloadFields convention.Shape` for the emptiness check, and when rebuilding set `Shape = MappingShape.Record fieldMappings`. (Explicit class targets a record type; a union with an explicit class keeps `Union []` until Task 5 — acceptable, no behaviour regression since unions had no explicit-class tests.)

- [ ] **Step 6: Update LockFile serialize/parse/merge/count**

In `src/Frank.Semantic/LockFile.fs`:

`serializeMapping` — replace the `fields` block with a shape-discriminated block:

```fsharp
    let private serializeCaseMapping (c: CaseMapping) : JsonObject =
        let obj = JsonObject()
        obj.Add("name", JsonValue.Create c.Name)
        obj.Add("iri", c.Iri |> Option.map JsonValue.Create<string> |> Option.toObj)
        obj.Add("confidence", JsonValue.Create c.Confidence)
        obj.Add("source", JsonValue.Create(mappingSourceToString c.Source))
        obj.Add("status", JsonValue.Create(mappingStatusToString c.Status))
        let payload = JsonArray()
        for f in c.Payload do payload.Add(serializeFieldMapping f)
        obj.Add("payload", payload)
        obj
```

In `serializeMapping`, after writing `alternates`, replace the `fields` array with:

```fsharp
        match m.Shape with
        | Record fs ->
            obj.Add("shape", JsonValue.Create "record")
            let fields = JsonArray()
            for f in fs do fields.Add(serializeFieldMapping f)
            obj.Add("fields", fields)
        | Union cases ->
            obj.Add("shape", JsonValue.Create "union")
            let arr = JsonArray()
            for c in cases do arr.Add(serializeCaseMapping c)
            obj.Add("cases", arr)
```

`parseMapping` — parse the shape. Add a case parser and shape dispatch:

```fsharp
    let private parseCaseMapping (node: JsonNode) : Result<CaseMapping, string> =
        requireString node "name"
        |> Result.bind (fun name ->
            let iri = optionalString node "iri"
            requireFloat node "confidence"
            |> Result.bind (fun confidence ->
                requireString node "source"
                |> Result.bind mappingSourceFromString
                |> Result.bind (fun source ->
                    requireString node "status"
                    |> Result.bind mappingStatusFromString
                    |> Result.bind (fun status ->
                        parseFieldMappings node.["payload"]
                        |> Result.map (fun payload ->
                            { Name = name
                              Iri = iri
                              Confidence = confidence
                              Source = source
                              Status = status
                              Payload = payload })))))

    let private parseCaseMappings (node: JsonNode) : Result<CaseMapping list, string> =
        match node with
        | null -> Ok []
        | :? JsonArray as elements ->
            elements
            |> Seq.mapi (fun i el -> parseCaseMapping el |> Result.mapError (fun e -> $"cases[{i}]: {e}"))
            |> Seq.fold
                (fun acc r ->
                    match acc, r with
                    | Error e, _ -> Error e
                    | _, Error e -> Error e
                    | Ok xs, Ok x -> Ok(x :: xs))
                (Ok [])
            |> Result.map List.rev
        | _ -> Error "field 'cases' must be an array"

    let private parseShape (node: JsonNode) : Result<MappingShape, string> =
        match optionalString node "shape" with
        | Some "union" -> parseCaseMappings node.["cases"] |> Result.map Union
        | Some "record"
        | None -> parseFieldMappings node.["fields"] |> Result.map Record
        | Some other -> Error $"unknown shape '{other}'"
```

In `parseMapping`, replace the `parseFieldMappings node.["fields"] |> Result.bind (fun fields -> ...)` segment so the record builds `Shape` from `parseShape node` and sets `Shape = shape` (drop the `Fields = fields` binding). The missing-`shape` default to record keeps existing locks readable.

`countByStatus` — unchanged (counts top-level mappings only). Leave as-is.

`merge` — `mergeFields`/`mergeOneMapping` reference `.Fields`. Replace `mergeOneMapping` to merge by shape:

```fsharp
    let private mergeShape (existing: MappingShape) (resolved: MappingShape) : MappingShape =
        match existing, resolved with
        | Record ef, Record rf -> Record(mergeFields ef rf)
        | Union ec, Union rc ->
            let rByName = rc |> List.map (fun c -> c.Name, c) |> Map.ofList
            ec
            |> List.map (fun c ->
                match Map.tryFind c.Name rByName with
                | Some r -> { r with Payload = mergeFields c.Payload r.Payload }
                | None -> c)
            |> Union
        | _ -> resolved // shape changed (type kind changed in source) — take resolved

    let private mergeOneMapping (existing: Mapping) (resolved: Mapping) : Mapping =
        { existing with
            Iri = resolved.Iri
            Confidence = resolved.Confidence
            Source = resolved.Source
            Status = resolved.Status
            Shape = mergeShape existing.Shape resolved.Shape }
```

(`mergeFields` stays as defined; it's reused above.)

- [ ] **Step 7: Update ResolvedModel Excluded-filter**

In `src/Frank.Semantic/ResolvedModel.fs` `buildResource` (line 181), `m.Fields` no longer exists. Filter Excluded across the shape, flattening to the record-field list the resolver already consumes:

```fsharp
            let includedFields =
                MappingShape.payloadFields m.Shape
                |> List.filter (fun f -> f.Status <> Excluded)
```

(Cases whose own `Status = Excluded` should not contribute payload either. Replace with:)

```fsharp
            let includedFields =
                (match m.Shape with
                 | Record fs -> fs
                 | Union cases ->
                     cases
                     |> List.filter (fun c -> c.Status <> Excluded)
                     |> List.collect (fun c -> c.Payload))
                |> List.filter (fun f -> f.Status <> Excluded)
```

Open `Frank.Semantic` is already in scope for `MappingShape`. Confirm `ResolvedField` building is unaffected (it consumes `FieldMapping`, unchanged).

- [ ] **Step 8: Update Finalize**

In `src/Frank.Cli.Core/Finalize.fs`, `decideMapping` references `m.Fields`. Decide payload fields *and* cases. Replace `decideMapping`:

```fsharp
let private decideCase (c: CaseMapping) : CaseMapping =
    let payload = c.Payload |> List.map decideField
    match c.Status with
    | Confirmed
    | Excluded -> { c with Payload = payload }
    | Proposed
    | Unresolved ->
        { c with
            Status = Excluded
            Payload = payload }

let private decideShape (shape: MappingShape) : MappingShape =
    match shape with
    | Record fs -> Record(List.map decideField fs)
    | Union cases -> Union(List.map decideCase cases)

let private decideMapping (m: Mapping) : Mapping =
    let shape = decideShape m.Shape

    match m.Status with
    | Confirmed
    | Excluded -> { m with Shape = shape }
    | Proposed
    | Unresolved ->
        { m with
            Status = Excluded
            Shape = shape }
```

- [ ] **Step 9: Update the MSBuild gate**

In `src/Frank.Cli.MSBuild/ValidateLockFileTask.fs`, `m.Fields` no longer exists. The gate must count undecided cases *and* payload fields (of non-excluded mappings/cases). Replace the `draftFields` computation (and add `draftCases`):

```fsharp
                let draftMappings = lock.Mappings |> List.filter (fun m -> not (isDecided m.Status))

                let liveMappings = lock.Mappings |> List.filter (fun m -> m.Status <> Excluded)

                let draftCases =
                    liveMappings
                    |> List.collect (fun m -> MappingShape.caseMappings m.Shape)
                    |> List.filter (fun c -> not (isDecided c.Status))

                let draftFields =
                    liveMappings
                    |> List.collect (fun m ->
                        match m.Shape with
                        | Record fs -> fs
                        | Union cases ->
                            cases
                            |> List.filter (fun c -> c.Status <> Excluded)
                            |> List.collect (fun c -> c.Payload))
                    |> List.filter (fun f -> not (isDecided f.Status))

                let total = draftMappings.Length + draftCases.Length + draftFields.Length
```

(`open Frank.Semantic` already gives `MappingShape`. The error message and the `if total > 0` block are unchanged.)

- [ ] **Step 10: Migrate all `Mapping` literal sites in tests + Emitter**

`SemanticModelEmitter.fs` reads `model.Resources` (ResolvedModel) and does NOT touch `Mapping.Fields` — but the emitter test fixtures (`probeLock`) build `Mapping` literals with `Fields = []`. Replace every `Mapping` literal's `Fields = [ ... ]` with `Shape = MappingShape.Record [ ... ]` (or `Union`) across:
- `test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs`
- `test/Frank.Semantic.Tests/ResolvedModelTests.fs` (`mkMapping` helper)
- `test/Frank.Cli.Core.Tests/FinalizeTests.fs` (`mapping` helper)
- `test/Frank.Cli.MSBuild.Tests/ValidateLockFileTaskTests.fs`
- any `LockFileTests.fs` fixtures

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; grep -rn "Fields =" --include=*.fs test src` and migrate each `Mapping`/`FieldMapping`-context hit. (`FieldMapping` has no `Fields`; only `Mapping` literals change. `StatusCounts` also has no `Fields`.)

- [ ] **Step 11: Build + test all four projects green**

Run:
```
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
rm -rf src/*/obj src/*/bin
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build src/Frank.Semantic/ src/Frank.Cli.Core/ src/Frank.Cli.MSBuild/
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ -v quiet
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ -v quiet
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.MSBuild.Tests/ -v quiet
```
Expected: PASS — union round-trip green; records behave identically; gate/finalize/resolve still green.

- [ ] **Step 12: Commit**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
git add src/ test/
git commit -m "feat(semantic): sum-aware Mapping shape across lock/resolve/finalize/gate"
```

---

## Task 5: The union join — case scoring

Now `score` emits real `CaseMapping`s: nullary case → individual, payload case → subclass, confirmed on whole-`normKey` exact match (reusing the type-level rule); payload fields via existing `buildFieldMapping`. No cross-entity composition (investment residue per spec assumption #5).

**Files:**
- Modify: `src/Frank.Semantic/ConventionEngine.fs` (`score`, new `buildCaseMapping`)
- Test: `test/Frank.Semantic.Tests/ConventionEngineTests.fs`

- [ ] **Step 1: Write failing tests (AT1/AT2/AT3 unit form)**

Add to `ConventionEngineTests.fs`. Use the file's existing helpers for building a `VocabularyRegistry` and `VocabTerms`; if a `mkRegistry`/`mkTerms` helper exists, reuse it, otherwise construct literals. Tests:

```fsharp
[<Tests>]
let unionJoinTests =
    testList
        "Union join (case scoring)"
        [ test "AT1: nullary case confirms against a declared individual" {
              let terms =
                  { Classes = Map.ofList [ "light", "https://ex.org/Light" ]
                    Properties = Map.empty
                    Individuals = Map.ofList [ "red", "https://ex.org/Red"; "green", "https://ex.org/Green" ] }

              let registry = mkRegistryUsing [ "ex", "https://ex.org/" ]

              let typeInfo =
                  { FullName = "App.Light"
                    Namespace = "App"
                    LocalName = "Light"
                    Shape = Union [ mkCase "Red" []; mkCase "Green" [] ]
                    Attributes = Map.empty
                    DocComment = None }

              let m = ConventionEngine.score terms registry typeInfo

              match m.Shape with
              | MappingShape.Union cases ->
                  let red = cases |> List.find (fun c -> c.Name = "Red")
                  Expect.equal red.Status Confirmed "Red confirmed"
                  Expect.equal red.Iri (Some "ex:Red") "Red → individual IRI"
              | _ -> failwith "expected Union shape"
          }

          test "AT2: payload case against generic vocab does NOT map to a property" {
              let terms =
                  { Classes = Map.ofList [ "move", "https://ex.org/Move" ]
                    Properties = Map.ofList [ "ordereditem", "https://ex.org/orderedItem" ]
                    Individuals = Map.empty }

              let registry = mkRegistryUsing [ "ex", "https://ex.org/" ]

              let typeInfo =
                  { FullName = "App.Move"
                    Namespace = "App"
                    LocalName = "Move"
                    Shape = Union [ mkCase "XMove" [ mkFieldInfo "position" "SquarePosition" ] ]
                    Attributes = Map.empty
                    DocComment = None }

              let m = ConventionEngine.score terms registry typeInfo

              match m.Shape with
              | MappingShape.Union cases ->
                  let x = cases |> List.find (fun c -> c.Name = "XMove")
                  Expect.notEqual x.Iri (Some "ex:orderedItem") "XMove must NOT map to a property"
                  Expect.notEqual x.Status Confirmed "no exact subclass → not confirmed"
              | _ -> failwith "expected Union shape"
          }

          test "AT3: payload case confirms against a declared subclass" {
              let terms =
                  { Classes = Map.ofList [ "move", "https://ex.org/Move"; "xmove", "https://ex.org/XMove" ]
                    Properties = Map.empty
                    Individuals = Map.empty }

              let registry = mkRegistryUsing [ "ex", "https://ex.org/" ]

              let typeInfo =
                  { FullName = "App.Move"
                    Namespace = "App"
                    LocalName = "Move"
                    Shape = Union [ mkCase "XMove" [ mkFieldInfo "position" "SquarePosition" ] ]
                    Attributes = Map.empty
                    DocComment = None }

              let m = ConventionEngine.score terms registry typeInfo

              match m.Shape with
              | MappingShape.Union cases ->
                  let x = cases |> List.find (fun c -> c.Name = "XMove")
                  Expect.equal x.Status Confirmed "XMove confirmed against subclass"
                  Expect.equal x.Iri (Some "ex:XMove") "XMove → subclass IRI"
              | _ -> failwith "expected Union shape"
          } ]
```

Add the small helpers near the top of the test file if absent:

```fsharp
let mkFieldInfo name typeName : FieldInfo =
    { Name = name; TypeName = typeName; Attributes = Map.empty; DocComment = None }

let mkCase name payload : CaseInfo =
    { Name = name; Payload = payload; Attributes = Map.empty; DocComment = None }

let mkRegistryUsing (prefixes: (string * string) list) : VocabularyRegistry =
    { Prefixes = prefixes |> List.map (fun (p, u) -> p, System.Uri u) |> Map.ofList
      Using = prefixes |> List.map fst |> Set.ofList
      EquivalentClasses = Map.empty
      SeeAlso = Map.empty
      FieldSeeAlso = Map.empty
      ProvClasses = Map.empty
      ConstraintPatterns = Map.empty }
```

- [ ] **Step 2: Run, verify fail**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ --filter "Union join" -v quiet`
Expected: FAIL — cases come back empty (`Union []`), so `List.find` throws / statuses wrong.

- [ ] **Step 3: Implement `buildCaseMapping` + wire into `score`**

Add to `ConventionEngine.fs` (after `buildFieldMapping`, before `applyExplicitClass`):

```fsharp
    /// Match a whole name against a role map (classes for payload cases,
    /// individuals for nullary cases) by normalized-key identity → Confirmed,
    /// else best fuzzy → Proposed, else Unresolved. Mirrors the type-level rule.
    let private matchEntity
        (prefixes: Map<string, Uri>)
        (entities: Map<string, string>)
        (name: string)
        : (string option * float * MappingStatus) =
        if entities.IsEmpty then
            None, 0.0, Unresolved
        else
            let key = normKey name

            match Map.tryFind key entities with
            | Some iri -> Some(toCurie prefixes (Uri iri)), 1.0, Confirmed
            | None ->
                let bestLocal, bestIri, conf =
                    entities
                    |> Map.toSeq
                    |> Seq.map (fun (k, iri) -> k, iri, jaroWinkler key k)
                    |> Seq.maxBy (fun (_, _, c) -> c)

                ignore bestLocal

                if conf > 0.0 then
                    Some(toCurie prefixes (Uri bestIri)), conf, Proposed
                else
                    None, 0.0, Unresolved

    let private buildCaseMapping
        (registry: VocabularyRegistry)
        (terms: VocabTerms)
        (case: CaseInfo)
        : CaseMapping =
        // nullary → individual; payload-carrying → subclass (a class).
        let role =
            if case.Payload.IsEmpty then
                terms.Individuals
            else
                terms.Classes
            |> Map.filter (fun _ iri -> isInScope registry iri)

        let iri, conf, status = matchEntity registry.Prefixes role case.Name

        let payload =
            case.Payload
            |> List.map (buildFieldMapping registry.Prefixes terms.Properties)

        { Name = case.Name
          Iri = iri
          Confidence = conf
          Source = Convention
          Status = status
          Payload = payload }
```

In `score`, replace the Task-4 placeholder shape computation:

```fsharp
                    let shape =
                        match typeInfo.Shape with
                        | TypeShape.Union cases ->
                            MappingShape.Union(cases |> List.map (buildCaseMapping registry terms))
                        | TypeShape.Record _ -> MappingShape.Record fieldMappings
```

Also update `emptyUnresolved`: when the type is a union but no in-scope classes exist for the *type itself*, the cases should still be scored. Replace the early `if inScopeClasses.IsEmpty then emptyUnresolved` so a union still maps its cases. Change the union branch to compute cases regardless:

```fsharp
        let conventionResult =
            if inScopeClasses.IsEmpty then
                match typeInfo.Shape with
                | TypeShape.Union cases ->
                    { emptyUnresolved with
                        Shape = MappingShape.Union(cases |> List.map (buildCaseMapping registry terms)) }
                | TypeShape.Record _ -> emptyUnresolved
            else
                // ... existing candidate scoring, using the `shape` binding above ...
```

(Set `emptyUnresolved`'s `Shape = MappingShape.Record []`.)

- [ ] **Step 4: Run, verify pass**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ --filter "Union join" -v quiet`
Expected: PASS — AT1 confirms individual, AT2 no property/no confirm, AT3 confirms subclass. Run the full `Frank.Semantic.Tests` to confirm no regression.

- [ ] **Step 5: Add generic/recursion graceful test (AT5)**

```fsharp
          test "AT5: generic and recursive unions map structurally without crash" {
              let terms = { Classes = Map.empty; Properties = Map.empty; Individuals = Map.empty }
              let registry = mkRegistryUsing [ "ex", "https://ex.org/" ]

              let result =
                  { FullName = "App.Result`2"
                    Namespace = "App"
                    LocalName = "Result"
                    Shape = Union [ mkCase "Ok" [ mkFieldInfo "Item" "'T" ]; mkCase "Error" [ mkFieldInfo "Item" "'TError" ] ]
                    Attributes = Map.empty
                    DocComment = None }

              let m = ConventionEngine.score terms registry result
              match m.Shape with
              | MappingShape.Union cases -> Expect.equal cases.Length 2 "both cases present, no crash"
              | _ -> failwith "expected Union"
          }
```

Run the filter again; expected PASS (empty maps → all Unresolved, no exception).

- [ ] **Step 6: Commit**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
git add src/Frank.Semantic/ConventionEngine.fs test/Frank.Semantic.Tests/ConventionEngineTests.fs
git commit -m "feat(semantic): union join — nullary→individual, payload→subclass, exact-confirm"
```

---

## Task 6: Codegen — per-union case match function (anti-drift over constructors)

For each mapped union, emit `<localNameCamel>CaseIri : <Type> -> System.Uri` matching each case constructor to its IRI. Exhaustiveness breaks the build on case rename.

**Files:**
- Modify: `src/Frank.Cli.Core/SemanticModelEmitter.fs`
- Modify: `src/Frank.Semantic/ResolvedModel.fs` (carry case IRIs through to the resolved model)
- Test: `test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs`

- [ ] **Step 1: Decide the resolved-model carrier**

`SemanticModelEmitter` consumes `ResolvedModel`, not `Mapping`. `ResolvedResource` has no case data. Add a `Cases` field carrying confirmed case constructors + IRIs. In `src/Frank.Semantic/ResolvedModel.fs`, extend `ResolvedResource` (after `Fields`):

```fsharp
type ResolvedCase =
    { CaseName: string
      Iri: Uri }

type ResolvedResource =
    { FSharpType: string
      LocalName: string
      GenericArity: int
      ClassIri: Uri option
      EquivalentClass: Uri option
      SeeAlso: Uri list
      ProvClass: ProvOClass option
      Fields: ResolvedField list
      Cases: ResolvedCase list }
```

In `buildResource`, populate `Cases` from confirmed union cases with a resolvable IRI:

```fsharp
                let cases =
                    match m.Shape with
                    | MappingShape.Union cs ->
                        cs
                        |> List.filter (fun c -> c.Status = Confirmed)
                        |> List.choose (fun c ->
                            match VocabularyRegistry.tryResolveIri prefixes c.Iri with
                            | Ok(Some iri) -> Some { CaseName = c.CaseName; Iri = iri }
                            | _ -> None)
                    | MappingShape.Record _ -> []
```

Wait — `CaseMapping` field is `Name`, not `CaseName`. Use `c.Name`:

```fsharp
                        |> List.choose (fun c ->
                            match VocabularyRegistry.tryResolveIri prefixes c.Iri with
                            | Ok(Some iri) -> Some { CaseName = c.Name; Iri = iri }
                            | _ -> None)
```

Add `Cases = cases` to the returned `ResolvedResource` literal. Update every other `ResolvedResource` literal in tests with `Cases = []`.

- [ ] **Step 2: Write failing emitter test**

Add to `test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs` — extend `probeLock` with a confirmed union and assert the emitted source contains the case match:

```fsharp
[<Tests>]
let unionCaseEmissionTests =
    testList
        "Union case emission"
        [ test "a confirmed union emits a CaseIri match over constructors" {
              let lock: LockFile =
                  { SchemaVersion = 1
                    Generated = System.DateTimeOffset.Parse "2025-01-01T00:00:00Z"
                    Vocabularies =
                      Map.ofList
                          [ "ex", { Uri = "https://ex.org/"; FetchedAt = System.DateTimeOffset.Parse "2025-01-01T00:00:00Z"; Hash = "sha256:t" } ]
                    Mappings =
                      [ { FSharpType = "Probe.Move"
                          Iri = Some "ex:Move"
                          Confidence = 1.0
                          Source = Convention
                          Status = Confirmed
                          Alternates = []
                          Shape =
                            Union
                                [ { Name = "XMove"; Iri = Some "ex:XMove"; Confidence = 1.0; Source = Convention; Status = Confirmed; Payload = [] }
                                  { Name = "OMove"; Iri = Some "ex:OMove"; Confidence = 1.0; Source = Convention; Status = Confirmed; Payload = [] } ] } ] }

              let registry = mkRegistryUsing [ "ex", "https://ex.org/" ] // reuse or inline a registry with the ex prefix
              let src = Expect.wantOk (SemanticModelEmitter.emit "Probe.Generated" registry lock) "emit"
              Expect.stringContains src "moveCaseIri" "case function name"
              Expect.stringContains src "| XMove _ -> System.Uri(\"https://ex.org/XMove\")" "XMove arm over constructor"
              Expect.stringContains src "| OMove _ -> System.Uri(\"https://ex.org/OMove\")" "OMove arm over constructor"
          } ]
```

(If the test file has no `mkRegistryUsing`, inline a `VocabularyRegistry` literal with `Prefixes`/`Using` for `ex`.)

- [ ] **Step 3: Run, verify fail**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "Union case emission" -v quiet`
Expected: FAIL — emitter does not render case functions.

- [ ] **Step 4: Render the case match function**

In `src/Frank.Cli.Core/SemanticModelEmitter.fs`, the `MappedResource` carries only class data. Extend it and render. Add to `MappedResource`:

```fsharp
type private MappedResource =
    { LocalName: string
      ClassIri: Uri
      FSharpType: string
      GenericArity: int
      Cases: ResolvedCase list }
```

Update `toMapped` to carry `Cases = r.Cases`. Add a renderer and a camel-case helper:

```fsharp
    let private camel (s: string) : string =
        if String.IsNullOrEmpty s then s
        else string (Char.ToLowerInvariant s.[0]) + s.[1..]

    /// Render `let <type>CaseIri (x: FSharpType) : System.Uri = match x with ...`
    /// over the REAL F# case constructors. Exhaustive → renaming a case breaks the build.
    let private renderCaseMatch (r: MappedResource) : string option =
        if r.Cases.IsEmpty then
            None
        else
            let fnName = camel r.LocalName + "CaseIri"

            let arms =
                r.Cases
                |> List.map (fun c -> "    | " + c.CaseName + " _ -> System.Uri(\"" + c.Iri.AbsoluteUri + "\")")
                |> String.concat "\n"

            Some(
                "let " + fnName + " (x: " + r.FSharpType + ") : System.Uri =\n    match x with\n" + arms
            )
```

In `assembleModule`, append the case matches after `renderClrTypeMatch`:

```fsharp
    let private assembleModule (moduleName: string) (resources: MappedResource list) : string =
        let caseMatches =
            resources |> List.choose renderCaseMatch |> String.concat "\n\n"

        String.concat
            "\n"
            [ "// <auto-generated> Anti-drift guard: compiled WITH the domain so renaming/removing a mapped type breaks the build. Not consumed at runtime. </auto-generated>"
              $"module {moduleName}"
              ""
              renderDuDecl resources
              ""
              renderIriMatch resources
              ""
              renderClrTypeMatch resources
              ""
              caseMatches
              "" ]
```

Note: payload cases use `| XMove _ ->`; nullary cases would be `| Red ->` (no `_`). For correctness with nullary constructors, the `_` wildcard works for *payload* cases but NOT for nullary ones (`Red _` is a compile error). Track nullary-ness on `ResolvedCase`:

In `ResolvedModel.fs` add `IsNullary: bool` to `ResolvedCase` (set from `c.Payload.IsEmpty` at build time — `CaseMapping.Payload`). Then in `renderCaseMatch`:

```fsharp
                |> List.map (fun c ->
                    let ctor = if c.IsNullary then c.CaseName else c.CaseName + " _"
                    "    | " + ctor + " -> System.Uri(\"" + c.Iri.AbsoluteUri + "\")")
```

Update the emitter test's `Payload = []` cases (they are nullary → arm is `| XMove ->`); adjust the test's expected strings to `"| XMove -> System.Uri"` accordingly, OR give the test cases a payload field so they render with `_`. Use a payload field in the test so the `_` arm is exercised (more representative): set each case `Payload = [ { Name = "position"; Iri = None; Confidence = 0.0; Source = Convention; Status = Unresolved } ]` and keep the `| XMove _ ->` expectation.

- [ ] **Step 5: Run, verify pass + full emitter suite**

Run:
```
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ -v quiet
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ -v quiet
```
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
git add src/Frank.Cli.Core/SemanticModelEmitter.fs src/Frank.Semantic/ResolvedModel.fs test/
git commit -m "feat(cli): emit per-union CaseIri match over constructors (anti-drift)"
```

---

## Task 7: Anti-drift build break (AT4) + TicTacToe sample re-bless

Prove the generated case match breaks the build on a case rename, and update the sample so the floor-E2E covers a real union.

**Files:**
- Modify: `sample/TicTacToe-v732/` domain + `.frank/` lock (re-bless Move as a union)
- Modify: `sample/TicTacToe-v732/test-floor-e2e.sh` (add an anti-drift case-rename assertion)

- [ ] **Step 1: Inspect the current sample + harness**

Run:
```
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
cat sample/TicTacToe-v732/test-floor-e2e.sh
ls sample/TicTacToe-v732/.frank/
sed -n '1,80p' sample/TicTacToe-v732/.frank/*.lock 2>/dev/null || true
```
Identify how `Move` is currently mapped (handoff: Move was re-blessed as an llm investment with `position`/`agent`). Confirm the domain `Move` type definition and the lock entry shape.

- [ ] **Step 2: Regenerate the lock from source**

With Tasks 1–6 in place, re-run the extract+convention pipeline the harness uses (the same CLI invocation the floor-E2E uses to produce the lock). Then `frank semantic finalize` (or the curated investment step the sample already documents) to reach all-decided. `rm -rf obj bin` first.

Run (adapt the exact CLI call from `test-floor-e2e.sh`):
```
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild/sample/TicTacToe-v732
rm -rf obj bin
# <the extract / generate / finalize commands the harness runs>
```
Verify the `Move` mapping is now `shape: "union"` with `cases` (XMove/OMove) rather than the old flat `fields`.

- [ ] **Step 3: Add the anti-drift assertion to the harness**

Append a step to `sample/TicTacToe-v732/test-floor-e2e.sh` (follow the file's existing bash-assertion style): after a clean generate+build succeeds, rename a `Move` case in the domain source, rebuild WITHOUT regenerating, assert the build fails referencing the generated `moveCaseIri`, then restore the source. Concretely:

```bash
echo "AT4: anti-drift — renaming a mapped case breaks the build"
cp src/Domain.fs src/Domain.fs.bak
sed -i.tmp 's/XMove/XPlay/' src/Domain.fs
rm -f src/Domain.fs.tmp
if DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build >/tmp/at4.log 2>&1; then
  echo "FAIL AT4: build succeeded after case rename (anti-drift broken)"
  mv src/Domain.fs.bak src/Domain.fs
  exit 1
fi
mv src/Domain.fs.bak src/Domain.fs
echo "PASS AT4: build broke on case rename"
```
(Adjust `src/Domain.fs` and the case name to the sample's actual file/case.)

- [ ] **Step 4: Run the full floor-E2E**

Run:
```
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild/sample/TicTacToe-v732
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 ./test-floor-e2e.sh
```
Expected: all prior assertions PASS + new AT4 PASS. If the sample's discovery output changed because `Move` is now a union, update the expected discovery artifact in the harness to match the new (correct) output — never weaken an assertion to pass; update it to the new correct value and note why.

- [ ] **Step 5: Full repo verification**

Run:
```
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
rm -rf src/*/obj src/*/bin
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.MSBuild.Tests/
dotnet fantomas --check src/
```
Expected: all green; fantomas clean.

- [ ] **Step 6: Commit**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/v732-rebuild
git add sample/TicTacToe-v732/
git commit -m "test(sample): re-bless Move as union; floor-E2E AT4 anti-drift case-rename break"
```

---

## Self-Review

**Spec coverage:**
- 3-layer model → Tasks 3 (structural extraction), 5 (lexical join), residue left Unresolved (gate forces decision). ✓
- Join framing (structure × vocab richness) → Task 5 AT2/AT3 (same code, vocab decides). ✓
- Three-part tokenizer (single-cap, acronym) → Task 1. ✓
- Match against individuals → Task 2 + Task 5 nullary path. ✓
- Sum-aware shape, no flatten → Tasks 3 (`TypeShape`) + 4 (`MappingShape`/`CaseMapping`). ✓
- Status model, no new status → Task 4 (reuses 4 statuses; gate/finalize per case). ✓
- Per-constructor anti-drift codegen → Task 6 + Task 7 AT4. ✓
- Edges: payload source priority → Task 3 `payloadFieldInfo`; exact-confirm → Task 5 `matchEntity`; generics/recursion → Task 5 AT5; no synthesized links → Task 5 matches one entity only. ✓

**Type consistency:** `CaseInfo`/`TypeShape` (Task 3) vs `CaseMapping`/`MappingShape` (Task 4) are distinct types with intentionally parallel `Record`/`Union` case names — qualify (`MappingShape.Union`) where inference needs help. `CaseMapping.Name` (not `CaseName`) used consistently; `ResolvedCase.CaseName`/`IsNullary` introduced in Task 6 and consumed only there. `recordFields` helper (Task 3) and `MappingShape.payloadFields`/`mapFields` (Task 4) are the two flatten helpers — named distinctly, used in different layers.

**Placeholder scan:** No "TBD"/"handle edge cases"/"similar to" — each step shows the code. The one parameterized spot (Task 7 CLI invocation) explicitly says "adapt from `test-floor-e2e.sh`" because the harness is the source of truth and must be read at execution; the surrounding assertions are concrete.
