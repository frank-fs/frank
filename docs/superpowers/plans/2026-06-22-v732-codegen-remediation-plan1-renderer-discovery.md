# v7.3.2 Codegen Remediation — Plan 1: Fabulous.AST Renderer + Discovery

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `DiscoveryEmitter`'s string-concatenation source assembly with a shared Fabulous.AST renderer, proving the AST-emission pattern end-to-end on the simplest emitter before the remaining emitters follow.

**Architecture:** A new `Frank.Cli.Core/AstRender.fs` exposes pure expression/module builders over Fabulous.AST 1.10.0 (Fantomas formats the output). `DiscoveryEmitter` keeps its pure projection (`ResolvedModel → ResolvedDescriptor list`) and swaps its `render*`/`assembleModule` string functions for `AstRender` calls. Tests move from substring assertions to: typed-projection equality (tier 1), and FCS-compile of the emitted source (tier 3) via a shared `typecheckTwoSources` helper.

**Tech Stack:** F# (net8.0/9.0/10.0), Fabulous.AST 1.10.0 (`open type Fabulous.AST.Ast`; `Gen.mkOak |> Gen.run`), Fantomas.Core 7.0.1 (transitive), Expecto, FSharp.Compiler.Service 43.10.103.

## Global Constraints

- Worktree root (absolute, cwd resets between Bash calls): `/Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation`. `cd` to it as the first statement of every command and confirm `git branch --show-current` is `v732-codegen-remediation`.
- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` prefix on every `dotnet` command.
- Test suites run by path (NOT in Frank.sln): `test/Frank.Cli.Core.Tests`.
- Baseline at HEAD `397514e2`: `Frank.Cli.Core.Tests` passes (DiscoveryEmitterTests 14 cases, LinkedDataEmitterTests 15, SemanticModelEmitterTests 8).
- No emitter may assemble F# source by string concatenation; emission is via Fabulous.AST. Substring assertions may remain only as cheap smoke, never the sole correctness gate.
- Commit after each task with the exact `git add` list given.

## Verified Fabulous.AST 1.10.0 API (run-confirmed 2026-06-22 — use verbatim)

```fsharp
open Fabulous.AST
open type Fabulous.AST.Ast
// Expr builders (WidgetBuilder<Fantomas.Core.SyntaxOak.Expr>):
ConstantExpr(String "s")          // → "s"   (quoted string literal)
ConstantExpr "None"               // → None  (bare identifier/constant; string overload, no String wrapper)
AppExpr("Some", e)                // → Some <e>
AppExpr("Uri", ConstantExpr(String "x"))   // → Uri "x"
RecordExpr [ RecordFieldExpr("F", e); ... ]   // → { F = <e>; ... }
ListExpr [ e1; e2 ]               // → [ <e1>; <e2> ]
Value("name", e).returnType("T")  // → let name: T = <e>
// String/ConstantExpr/RecordFieldExpr/AppExpr have string|Constant|Expr overloads —
// annotate helper params (id: string) so the string overload resolves (else FS0041 ambiguity).
// Top-level named module "A.B.Name" is emitted as namespace + nested module:
Oak() { Namespace "A.B" { Module "Name" { Open "X"; <bindings> } } } |> Gen.mkOak |> Gen.run
// Output of the above with a DiscoveryConfig value is valid F# (verified):
//   namespace A.B
//   module Name =
//       open X
//       let discoveryConfig: DiscoveryConfig = { ... }
```

## File Structure

- **Create** `src/Frank.Cli.Core/AstRender.fs` — pure Fabulous.AST expr + module builders. One responsibility: typed F# values → formatted source string.
- **Modify** `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` — add `Fabulous.AST` PackageReference; add `AstRender.fs` to `<Compile>` after `Finalize.fs`, before `DiscoveryEmitter.fs`.
- **Modify** `src/Frank.Cli.Core/DiscoveryEmitter.fs` — delete `escapeString`/`renderDescriptor`/`renderDescriptorList`/`renderLinkList`/`assembleModule`; keep `localName`/`typeDescriptor`/`fieldDescriptors`/`collectDescriptors`/`collectDescribedByLinks`/`ResolvedDescriptor`; expose internal `projectDiscovery`; rewrite `emit` to build the `DiscoveryConfig` value via `AstRender`.
- **Create** `test/Frank.Cli.Core.Tests/FcsTypecheck.fs` — `typecheckTwoSources` extracted from `SemanticModelEmitterTests.fs` (shared compile-gate helper).
- **Modify** `test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs` — delete its private `typecheckTwoSources`, open the shared module.
- **Create** `test/Frank.Cli.Core.Tests/AstRenderTests.fs` — round-trip exact-output tests for `AstRender`.
- **Modify** `test/Frank.Cli.Core.Tests/DiscoveryEmitterTests.fs` — add tier-1 projection-equality test + tier-3 compile-gate test; keep one substring smoke.
- **Modify** `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj` — add `FcsTypecheck.fs` (before `SemanticModelEmitterTests.fs`/`DiscoveryEmitterTests.fs`) and `AstRenderTests.fs`.

---

### Task 1: Add Fabulous.AST + create the `AstRender` renderer

**Files:**
- Modify: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
- Create: `src/Frank.Cli.Core/AstRender.fs`
- Create: `test/Frank.Cli.Core.Tests/AstRenderTests.fs`
- Modify: `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj`

**Interfaces:**
- Produces (consumed by Task 3 and later emitter plans):
  - `AstRender.strExpr : string -> WidgetBuilder<Expr>`
  - `AstRender.noneExpr : WidgetBuilder<Expr>`
  - `AstRender.someStrExpr : string -> WidgetBuilder<Expr>`
  - `AstRender.uriExpr : string -> WidgetBuilder<Expr>`
  - `AstRender.recordExpr : (string * WidgetBuilder<Expr>) list -> WidgetBuilder<Expr>`
  - `AstRender.listExpr : WidgetBuilder<Expr> list -> WidgetBuilder<Expr>`
  - `AstRender.formatTypedValueModule : moduleName:string -> opens:string list -> valueName:string -> typeName:string -> value:WidgetBuilder<Expr> -> string`
  (`WidgetBuilder<Expr>` = `Fabulous.AST.WidgetBuilder<Fantomas.Core.SyntaxOak.Expr>`.)

- [ ] **Step 1: Add the package reference.** In `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`, inside the existing `<ItemGroup>` that holds `<PackageReference>`, add:

```xml
<PackageReference Include="Fabulous.AST" Version="1.10.0" />
```

- [ ] **Step 2: Add `AstRender.fs` to the compile order.** In the same `.fsproj`, in the `<ItemGroup>` of `<Compile>` items, insert immediately after `<Compile Include="Finalize.fs" />` and before `<Compile Include="DiscoveryEmitter.fs" />`:

```xml
<Compile Include="AstRender.fs" />
```

- [ ] **Step 3: Write the failing round-trip test.** Create `test/Frank.Cli.Core.Tests/AstRenderTests.fs`:

```fsharp
module Frank.Cli.Core.Tests.AstRenderTests

open Expecto
open Frank.Cli.Core

[<Tests>]
let astRenderTests =
    testList
        "AstRender"
        [ test "formatTypedValueModule emits namespace + nested module + typed record value" {
              let value =
                  AstRender.recordExpr
                      [ "ProfileUri", AstRender.strExpr "/alps/tictactoe"
                        "AlpsDescriptors",
                        AstRender.listExpr
                            [ AstRender.recordExpr
                                  [ "Id", AstRender.strExpr "MoveAction"
                                    "Type", AstRender.strExpr "semantic"
                                    "Doc", AstRender.noneExpr
                                    "Href", AstRender.someStrExpr "https://schema.org/MoveAction" ] ] ]

              let src =
                  AstRender.formatTypedValueModule
                      "TicTacToe.GeneratedDiscovery"
                      [ "Frank.Discovery" ]
                      "discoveryConfig"
                      "DiscoveryConfig"
                      value

              let expected =
                  "namespace TicTacToe\n\nmodule GeneratedDiscovery =\n    open Frank.Discovery\n\n    let discoveryConfig: DiscoveryConfig =\n        { ProfileUri = \"/alps/tictactoe\"\n          AlpsDescriptors =\n            [ { Id = \"MoveAction\"\n                Type = \"semantic\"\n                Doc = None\n                Href = Some \"https://schema.org/MoveAction\" } ] }\n"

              Expect.equal src expected "byte-exact Fantomas-formatted module"
          }

          test "uriExpr renders Uri applied to a string literal" {
              let src =
                  AstRender.formatTypedValueModule "A.B" [] "x" "System.Uri" (AstRender.uriExpr "https://schema.org/X")

              Expect.stringContains src "let x: System.Uri = Uri \"https://schema.org/X\"" "Uri application"
          }

          test "two calls are byte-identical (determinism)" {
              let mk () =
                  AstRender.formatTypedValueModule "A.B" [] "x" "int" (AstRender.strExpr "1")

              Expect.equal (mk ()) (mk ()) "deterministic output"
          } ]
```

Add `<Compile Include="AstRenderTests.fs" />` to `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj` (anywhere after the project reference; before `Main.fs`/`Program.fs` if one exists as the last entry).

- [ ] **Step 4: Run — fails (AstRender undefined).**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "AstRender"`
Expected: FAIL — `The namespace or module 'AstRender' is not defined` (or build error).

- [ ] **Step 5: Implement `AstRender.fs`.** Create `src/Frank.Cli.Core/AstRender.fs`:

```fsharp
module Frank.Cli.Core.AstRender

open Fabulous.AST
open Fantomas.Core.SyntaxOak
open type Fabulous.AST.Ast

// ── Expression primitives (all yield WidgetBuilder<Expr>) ─────────────────────

/// A double-quoted string literal: "s"
let strExpr (s: string) : WidgetBuilder<Expr> = ConstantExpr(String s)

/// The bare identifier None
let noneExpr : WidgetBuilder<Expr> = ConstantExpr "None"

/// Some "<s>"
let someStrExpr (s: string) : WidgetBuilder<Expr> = AppExpr("Some", ConstantExpr(String s))

/// System.Uri applied to a string literal: Uri "<s>"
let uriExpr (s: string) : WidgetBuilder<Expr> = AppExpr("Uri", ConstantExpr(String s))

/// A record literal: { name1 = e1; name2 = e2; ... }
let recordExpr (fields: (string * WidgetBuilder<Expr>) list) : WidgetBuilder<Expr> =
    RecordExpr [ for (name, e) in fields -> RecordFieldExpr(name, e) ]

/// A list literal: [ e1; e2; ... ]
let listExpr (items: WidgetBuilder<Expr> list) : WidgetBuilder<Expr> = ListExpr items

// ── Module assembly ───────────────────────────────────────────────────────────

/// Split "A.B.Name" into ("A.B", "Name"); a dotless name becomes ("", name).
let private splitModuleName (moduleName: string) : string * string =
    match moduleName.LastIndexOf '.' with
    | -1 -> "", moduleName
    | i -> moduleName.[.. i - 1], moduleName.[i + 1 ..]

/// Render `namespace <ns>` + `module <name> = <opens> let <valueName>: <typeName> = <value>`.
/// Precondition: moduleName contains at least one '.' (RootNamespace-qualified, per the MSBuild targets).
let formatTypedValueModule
    (moduleName: string)
    (opens: string list)
    (valueName: string)
    (typeName: string)
    (value: WidgetBuilder<Expr>)
    : string =
    if moduleName.LastIndexOf '.' < 0 then
        invalidArg (nameof moduleName) "moduleName must be namespace-qualified (contain a '.')"

    let nsName, modName = splitModuleName moduleName

    let oak =
        Oak() {
            Namespace nsName {
                Module modName {
                    for o in opens do
                        Open o

                    Value(valueName, value).returnType (typeName)
                }
            }
        }

    oak |> Gen.mkOak |> Gen.run
```

> Implementer note: the verified facts in "Verified Fabulous.AST 1.10.0 API" are exact for 1.10.0. If a `WidgetBuilder<Expr>` annotation or the `for o in opens do Open o` CE form needs a minor adjustment to compile, adjust to satisfy the round-trip test in Step 3 — the test's `expected` string (copied from a run-verified spike) is the contract; do not weaken it. If `formatTypedValueModule "A.B" []` (no opens) trips the `for` loop, keep the loop (an empty list yields nothing).

- [ ] **Step 6: Run — passes.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "AstRender"`
Expected: PASS (3 tests).

- [ ] **Step 7: Fantomas + commit.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && dotnet fantomas src/Frank.Cli.Core/AstRender.fs test/Frank.Cli.Core.Tests/AstRenderTests.fs`

```bash
git add src/Frank.Cli.Core/Frank.Cli.Core.fsproj src/Frank.Cli.Core/AstRender.fs test/Frank.Cli.Core.Tests/AstRenderTests.fs test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj
git commit -m "feat(cli): AstRender — Fabulous.AST module/expr builders (no string concat)"
```

---

### Task 2: Extract `typecheckTwoSources` to a shared compile-gate helper

**Files:**
- Create: `test/Frank.Cli.Core.Tests/FcsTypecheck.fs`
- Modify: `test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs` (delete its private copy; open the shared module)
- Modify: `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj`

**Interfaces:**
- Produces: `FcsTypecheck.typecheckTwoSources : domainSrc:string -> emittedSrc:string -> string list` (returns FCS error-diagnostic messages; empty = clean compile).

- [ ] **Step 1: Read the existing helper.** Open `test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs` lines 75-110 and copy the body of the private `typecheckTwoSources` verbatim (it uses `FSharp.Compiler.Service` `ParseAndCheckProject`; domainSrc compiled first, emittedSrc second).

- [ ] **Step 2: Create the shared module.** Create `test/Frank.Cli.Core.Tests/FcsTypecheck.fs`:

```fsharp
module Frank.Cli.Core.Tests.FcsTypecheck

// (paste the exact opens the original used: FSharp.Compiler.CodeAnalysis, FSharp.Compiler.Text, System.IO, etc.)

/// Typecheck two F# sources together via FCS ParseAndCheckProject.
/// domainSrc declares the domain types; emittedSrc uses them.
/// Returns the error-severity diagnostic messages (empty list = clean compile).
let typecheckTwoSources (domainSrc: string) (emittedSrc: string) : string list =
    // (paste the verbatim body copied in Step 1)
    failwith "paste body"
```

Replace the `failwith` line with the exact copied body. Add `<Compile Include="FcsTypecheck.fs" />` to `Frank.Cli.Core.Tests.fsproj` BEFORE `SemanticModelEmitterTests.fs` and `DiscoveryEmitterTests.fs`.

- [ ] **Step 3: Point `SemanticModelEmitterTests` at the shared helper.** In `test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs`, delete the private `let private typecheckTwoSources …` definition, and add near the top opens:

```fsharp
open Frank.Cli.Core.Tests.FcsTypecheck
```

- [ ] **Step 4: Run the SemanticModel suite — unchanged green.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "SemanticModel"`
Expected: PASS, 8 tests (unchanged — pure extraction).

- [ ] **Step 5: Commit.**

```bash
git add test/Frank.Cli.Core.Tests/FcsTypecheck.fs test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj
git commit -m "test(cli): extract typecheckTwoSources to shared FcsTypecheck module"
```

---

### Task 3: Migrate `DiscoveryEmitter` to `AstRender` (typed projection + compile gate)

**Files:**
- Modify: `src/Frank.Cli.Core/DiscoveryEmitter.fs`
- Modify: `test/Frank.Cli.Core.Tests/DiscoveryEmitterTests.fs`

**Interfaces:**
- Consumes: `AstRender.*` (Task 1), `FcsTypecheck.typecheckTwoSources` (Task 2).
- Produces: `DiscoveryEmitter.emit` (signature unchanged: `moduleName:string -> profileUri:string -> registry:VocabularyRegistry -> lock:LockFile -> Result<string,string>`); new internal `DiscoveryEmitter.projectDiscovery : profileUri:string -> ResolvedModel -> ResolvedDescriptor list * string list` (descriptors, describedByLinks).

- [ ] **Step 1: Write the failing tier-1 projection test.** In `test/Frank.Cli.Core.Tests/DiscoveryEmitterTests.fs`, add (use the existing fixture registry/lock in that file; mirror their shape):

```fsharp
test "projectDiscovery yields typed descriptors for class + fields (tier 1)" {
    let model = ResolvedModel.build fixtureRegistry fixtureLock |> okOrFail
    let descriptors, links = DiscoveryEmitter.projectDiscovery "/alps/tictactoe" model
    Expect.contains (descriptors |> List.map (fun d -> d.Id)) "MoveAction" "type descriptor present"
    Expect.contains (descriptors |> List.map (fun d -> d.Href)) (Some "https://schema.org/MoveAction") "type href present"
    Expect.isNonEmpty links "describedBy links present"
}
```

(If `ResolvedDescriptor`/`projectDiscovery` are not yet visible to the test, that is expected — Step 3 exposes them as `internal` and `InternalsVisibleTo` already grants the test project access.)

- [ ] **Step 2: Write the failing tier-3 compile-gate test.** In the same file add:

```fsharp
test "emitted GeneratedDiscovery compiles against Frank.Discovery types (tier 3)" {
    let src = DiscoveryEmitter.emit "Probe.GeneratedDiscovery" "/alps/tictactoe" fixtureRegistry fixtureLock |> okOrFail
    // domainSrc: a minimal stand-in declaring the DiscoveryConfig/AlpsDescriptor shape the emitted value uses.
    let domainSrc =
        "namespace Frank.Discovery\n" +
        "type AlpsDescriptor = { Id: string; Type: string; Doc: string option; Href: string option }\n" +
        "type DiscoveryConfig = { ProfileUri: string; HomeRoute: string; AlpsDescriptors: AlpsDescriptor list; DescribedByLinks: string list }\n"
    let diagnostics = FcsTypecheck.typecheckTwoSources domainSrc src
    Expect.isEmpty diagnostics "emitted Discovery module compiles cleanly"
}
```

> Implementer: confirm the real `DiscoveryConfig`/`AlpsDescriptor` field names/types in `src/Frank.Discovery` and make `domainSrc` match them exactly (the emitted record literal must satisfy this shape). If `Frank.Discovery` is referenced by the test project, prefer typechecking the emitted source against the real referenced assembly instead of a `domainSrc` stand-in; otherwise the stand-in above is acceptable.

- [ ] **Step 3: Run — fails.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "tier"`
Expected: FAIL (projectDiscovery undefined / compile-gate red).

- [ ] **Step 4: Rewrite `DiscoveryEmitter.fs`.** Keep `localName`, `typeDescriptor`, `fieldDescriptors`, `collectDescriptors`, `collectDescribedByLinks`, and the `ResolvedDescriptor` type. Make `ResolvedDescriptor` and a new `projectDiscovery` `internal`. Delete `escapeString`, `renderDescriptor`, `renderDescriptorList`, `renderLinkList`, `assembleModule`. Replace `emit`'s tail:

```fsharp
// ResolvedDescriptor becomes internal so tests can assert on it
type internal ResolvedDescriptor = { Id: string; Href: string }   // (keep existing fields)

// ... keep localName / typeDescriptor / fieldDescriptors / collectDescriptors / collectDescribedByLinks ...

/// Pure projection: model → (descriptors, describedBy links). Testable typed output.
let internal projectDiscovery (_profileUri: string) (model: ResolvedModel) : ResolvedDescriptor list * string list =
    collectDescriptors model.Resources, collectDescribedByLinks model.Resources

// ── Source rendering via AstRender (no string concat) ─────────────────────────

let private descriptorExpr (d: ResolvedDescriptor) =
    AstRender.recordExpr
        [ "Id", AstRender.strExpr d.Id
          "Type", AstRender.strExpr "semantic"
          "Doc", AstRender.noneExpr
          "Href", AstRender.someStrExpr d.Href ]

let private configExpr (profileUri: string) (descriptors: ResolvedDescriptor list) (links: string list) =
    AstRender.recordExpr
        [ "ProfileUri", AstRender.strExpr profileUri
          "HomeRoute", AstRender.strExpr "/"
          "AlpsDescriptors", AstRender.listExpr (descriptors |> List.map descriptorExpr)
          "DescribedByLinks", AstRender.listExpr (links |> List.map AstRender.strExpr) ]

let emit
    (moduleName: string)
    (profileUri: string)
    (registry: VocabularyRegistry)
    (lock: LockFile)
    : Result<string, string> =
    if String.IsNullOrWhiteSpace moduleName then
        invalidArg (nameof moduleName) "moduleName must not be empty"
    if String.IsNullOrWhiteSpace profileUri then
        invalidArg (nameof profileUri) "profileUri must not be empty"

    match ResolvedModel.build registry lock with
    | Error e -> Error e
    | Ok model ->
        let descriptors, links = projectDiscovery profileUri model
        let value = configExpr profileUri descriptors links
        Ok(AstRender.formatTypedValueModule moduleName [ "Frank.Discovery" ] "discoveryConfig" "DiscoveryConfig" value)
```

> Implementer: match the real `ResolvedDescriptor` field set and `DiscoveryConfig` field names/order exactly (Step 2). The current `renderDescriptor` shows the field set (`Id`, `Type="semantic"`, `Doc=None`, `Href=Some …`); preserve it. Add `open Frank.Cli.Core` is unnecessary (same module namespace); `AstRender` is in the same assembly.

- [ ] **Step 5: Run the new tier tests — pass.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "tier"`
Expected: PASS.

- [ ] **Step 6: Reconcile the existing substring tests.** Run the full Discovery suite:

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "Discovery"`
The Fantomas-formatted output still contains the same tokens (`sh:`-free, the IRIs, `DiscoveryConfig`, `discoveryConfig`, `Frank.Discovery`), so most substring assertions still pass. For any that asserted exact whitespace/indentation produced by the old string assembler, update the expected substring to match the new (Fantomas) output — verify by printing the emitted source; do NOT delete a test or weaken an IRI/`urn:frank:` assertion. Keep at least one substring smoke test (`Expect.stringContains src "https://schema.org/MoveAction"`) and the `no urn:frank:` test.
Expected: PASS (all Discovery tests).

- [ ] **Step 7: Full Cli.Core suite + fantomas + commit.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ && dotnet fantomas --check src/Frank.Cli.Core/DiscoveryEmitter.fs src/Frank.Cli.Core/AstRender.fs`
Expected: PASS (whole suite); fantomas clean.

```bash
git add src/Frank.Cli.Core/DiscoveryEmitter.fs test/Frank.Cli.Core.Tests/DiscoveryEmitterTests.fs
git commit -m "refactor(cli): DiscoveryEmitter emits via AstRender; typed-projection + compile-gate tests"
```

---

## Self-Review

- **Spec coverage:** Plan 1 covers spec ACs #1 (no string assembly — Discovery), partial #5/#6 (compile gate + determinism for Discovery), #7 (no urn:frank: preserved). Remaining ACs (#2 ShapeDecl/illegal-states, #3 buildShapesGraph, #4 SHACL semantic, full #5 across emitters, LinkedData/SemanticModel/Validation) are deferred to Plans 2–4 by design (iterative). The "data DUs in Frank.Semantic" / term layer is introduced in the plan that first needs it (LinkedData=Plan 3, Validation=Plan 4), per YAGNI.
- **Placeholder scan:** none — the one `failwith "paste body"` in Task 2 Step 2 is an explicit copy-instruction with the source location given (Task 2 Step 1), not a deferral.
- **Type consistency:** `AstRender` signatures in Task 1 match their use in Task 3 (`recordExpr`/`listExpr`/`strExpr`/`noneExpr`/`someStrExpr`/`formatTypedValueModule`). `typecheckTwoSources` signature consistent Task 2 ↔ Task 3.

## Next plans (after Plan 1 is green + reviewed)

- **Plan 2:** SemanticModel mechanism-only (Union/UnionCase/MatchExpr AST; reuse `typecheckTwoSources` drift test).
- **Plan 3:** LinkedData typed artifact — adds `Frank.Semantic` triple-assertion helper + `OntologyDecl` DU + `Ontology` interpreter in `Frank.LinkedData`; emitter renders `OntologyDecl` via AstRender.
- **Plan 4:** Validation fresh build — cherry-pick `enrichTypes` (`169fe69d`); add `XsdDatatype`/`NonEmptyList`/`ShapeDecl` to `Frank.Semantic`; create `Frank.Validation` package + `Shapes.toShapesGraph` interpreter + `ValidationEmitter` + `GenerateValidationTask` + targets + resolver; tier-2 SHACL conformance tests.
