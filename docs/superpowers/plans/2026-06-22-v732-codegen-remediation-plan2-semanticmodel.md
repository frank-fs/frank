# v7.3.2 Codegen Remediation — Plan 2: SemanticModel via Fabulous.AST

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate `SemanticModelEmitter` from string-concatenation source assembly to Fabulous.AST, extending the shared `AstRender` with union-declaration and match-function builders. Mechanism-only: the emitted artifact (a `SemanticResource` DU + `iri`/`clrType`/`<type>CaseIri` functions, compiled WITH the domain as an anti-drift guard) is unchanged.

**Architecture:** Extend `AstRender` (from Plan 1) with `unionDecl`, `matchFunction`, `rawExpr`, and a multi-declaration `formatModule`. `SemanticModelEmitter` keeps its projection (`toMapped`, `clrTypeExpr` — which computes the `typeof<…>`/`typedefof<…>` STRING) and swaps `renderDuDecl`/`renderIriMatch`/`renderClrTypeMatch`/`renderCaseMatch`/`assembleModule` for `AstRender` calls. The load-bearing gate stays the existing FCS anti-drift test (rename a mapped domain type → emitted module fails to compile).

**Tech Stack:** F# net8/9/10, Fabulous.AST 1.10.0, Fantomas.Core 7.0.1, Expecto, FSharp.Compiler.Service 43.10.103.

## Global Constraints

- Worktree root (ABSOLUTE; cwd RESETS between Bash calls): `/Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation`. `cd` to it first in every command; confirm `git branch --show-current` is `v732-codegen-remediation`.
- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` on every `dotnet` command.
- Test suite (run by path): `test/Frank.Cli.Core.Tests`. Baseline at the start of Plan 2: 164 pass.
- No F# source assembled by string concatenation; emission via Fabulous.AST only. The `clrTypeExpr` helper computes a `typeof<…>` STRING — that is a single leaf-expression token rendered through `AstRender.rawExpr` (Fabulous.AST `ConstantExpr`), NOT source assembly; it is allowed. The module structure, the DU, and the match functions are all AST.
- Substring assertions only as cheap smoke, never the sole gate. The anti-drift FCS gate (`FcsTypecheck.typecheckTwoSources`) is the primary correctness gate for this emitter.
- Commit after each task with the exact `git add` list.

## Verified Fabulous.AST 1.10.0 API for this plan (run-confirmed 2026-06-22 — use verbatim)

```fsharp
open Fabulous.AST
open type Fabulous.AST.Ast
// DU type declaration:
Union("SemanticResource") { UnionCase("Move"); UnionCase("Game") }
//   → type SemanticResource =\n        | Move\n        | Game
// A let-function with ONE parenthesized typed parameter and a return type:
Function("iri", [ ParenPat(ParameterPat("r", "SemanticResource")) ], bodyExpr, "System.Uri")
//   → let iri (r: SemanticResource) : System.Uri = <bodyExpr>
//   NOTE: the param MUST be wrapped ParenPat(ParameterPat(name, type)) — bare ParameterPat omits the
//   parentheses and produces invalid F#. bodyExpr is arg 3 (a WidgetBuilder<Expr>); returnType is arg 4 (string).
// match expression:
MatchExpr("r", [ MatchClauseExpr("Move", e1); MatchClauseExpr("Game", e2); MatchClauseExpr("_", e3) ])
//   → match r with | Move -> <e1> | Game -> <e2> | _ -> <e3>
//   The clause pattern is a STRING: "Move", "Active _" (payload case), "_" (wildcard).
ConstantExpr "typeof<TicTacToe.Move>"   // → typeof<TicTacToe.Move>  (raw token; the escape hatch for typeof/typedefof)
AppExpr("Some", AppExpr("System.Uri", ConstantExpr(String iri)))   // → Some(System.Uri "<iri>")
// Module with multiple declarations + Gen pipeline (verified to render valid F#):
Oak() { Namespace "TicTacToe" { Module "GeneratedSemanticModel" { <decl1>; <decl2>; ... } } } |> Gen.mkOak |> Gen.run
```

## File Structure

- **Modify** `src/Frank.Cli.Core/AstRender.fs` — add `rawExpr`, `unionDecl`, `matchFunction`, and a multi-decl `formatModule` (keep the Plan 1 functions).
- **Modify** `test/Frank.Cli.Core.Tests/AstRenderTests.fs` — add round-trip tests for the new builders.
- **Modify** `src/Frank.Cli.Core/SemanticModelEmitter.fs` — keep projection (`toMapped`, `clrTypeExpr`, `camel`); delete `renderDuDecl`/`renderIriMatch`/`renderClrTypeMatch`/`renderCaseMatch`/`assembleModule`; rewrite `emit` via `AstRender`; expose internal `projectMapped` for tier-1.
- **Modify** `test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs` — add tier-1 projection test; keep the FCS anti-drift gate (already uses `FcsTypecheck`); reconcile substring tests to the new Fantomas output.

---

### Task 1: Extend `AstRender` with union + match-function builders

**Files:**
- Modify: `src/Frank.Cli.Core/AstRender.fs`
- Modify: `test/Frank.Cli.Core.Tests/AstRenderTests.fs`

**Interfaces:**
- Produces (consumed by Task 2):
  - `AstRender.rawExpr : string -> WidgetBuilder<Expr>` (renders the string verbatim as a token — for `typeof<…>`)
  - `AstRender.unionDecl : name:string -> cases:string list -> <module-decl widget>`
  - `AstRender.matchFunction : name:string -> paramName:string -> paramType:string -> returnType:string -> clauses:(string * WidgetBuilder<Expr>) list -> <module-decl widget>`
  - `AstRender.formatModule : moduleName:string -> leadingComment:string option -> opens:string list -> decls:<module-decl widget> list -> string`
  (The exact `<module-decl widget>` type is whatever the Fabulous.AST `Module` CE accepts for `Union`/`Function`. Determine it while making the round-trip test pass; the verified facts above give the builder calls.)

- [ ] **Step 1: Write the failing round-trip test.** Add to `test/Frank.Cli.Core.Tests/AstRenderTests.fs`:

```fsharp
test "formatModule emits a DU + two match functions with typeof, byte-exact" {
    let iri =
        AstRender.matchFunction "iri" "r" "SemanticResource" "System.Uri"
            [ "Move", AstRender.appExpr "System.Uri" (AstRender.strExpr "https://schema.org/MoveAction")
              "Game", AstRender.appExpr "System.Uri" (AstRender.strExpr "https://schema.org/Game") ]
    let clr =
        AstRender.matchFunction "clrType" "r" "SemanticResource" "System.Type"
            [ "Move", AstRender.rawExpr "typeof<TicTacToe.Move>"
              "Game", AstRender.rawExpr "typedefof<TicTacToe.Game<_>>" ]
    let du = AstRender.unionDecl "SemanticResource" [ "Move"; "Game" ]
    let src = AstRender.formatModule "TicTacToe.GeneratedSemanticModel" None [] [ du; iri; clr ]
    let expected =
        "namespace TicTacToe\n\nmodule GeneratedSemanticModel =\n    type SemanticResource =\n        | Move\n        | Game\n\n    let iri (r: SemanticResource) : System.Uri =\n        match r with\n        | Move -> System.Uri \"https://schema.org/MoveAction\"\n        | Game -> System.Uri \"https://schema.org/Game\"\n\n    let clrType (r: SemanticResource) : System.Type =\n        match r with\n        | Move -> typeof<TicTacToe.Move>\n        | Game -> typedefof<TicTacToe.Game<_>>\n"
    Expect.equal src expected "byte-exact DU + match functions"
}

test "formatModule prepends a leading comment when provided" {
    let du = AstRender.unionDecl "R" [ "A" ]
    let src = AstRender.formatModule "N.GeneratedSemanticModel" (Some "// <auto-generated> guard </auto-generated>") [] [ du ]
    Expect.stringContains src "// <auto-generated> guard </auto-generated>" "comment present"
    Expect.stringContains src "type R =" "DU present after comment"
}
```

> The `expected` string is copied from run-verified Fabulous.AST output (2026-06-22). If a builder's exact widget type needs an annotation to compile, adjust the implementation — never the `expected` string. Also add `AstRender.appExpr : func:string -> arg:WidgetBuilder<Expr> -> WidgetBuilder<Expr>` (wrapping `AppExpr(func, arg)`) since the test uses it; if Plan 1 did not expose it, add it now.

- [ ] **Step 2: Run — fails** (new AstRender members undefined).

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "AstRender"`

- [ ] **Step 3: Implement the new `AstRender` members.** Append to `src/Frank.Cli.Core/AstRender.fs`:

```fsharp
/// A verbatim token expression (e.g. "typeof<T>"). The escape hatch for type applications.
let rawExpr (s: string) : WidgetBuilder<Expr> = ConstantExpr s

/// A function application: <func> <arg>, e.g. appExpr "System.Uri" (strExpr "x") → System.Uri "x"
let appExpr (func: string) (arg: WidgetBuilder<Expr>) : WidgetBuilder<Expr> = AppExpr(func, arg)

/// A discriminated-union type declaration: type <name> = | c1 | c2 ...
let unionDecl (name: string) (cases: string list) =
    Union(name) {
        for c in cases do
            UnionCase(c)
    }

/// let <name> (<paramName>: <paramType>) : <returnType> = match <paramName> with <clauses>
let matchFunction
    (name: string)
    (paramName: string)
    (paramType: string)
    (returnType: string)
    (clauses: (string * WidgetBuilder<Expr>) list)
    =
    let body =
        MatchExpr(paramName, [ for (pat, e) in clauses -> MatchClauseExpr(pat, e) ])
    Function(name, [ ParenPat(ParameterPat(paramName, paramType)) ], body, returnType)

/// Render a module with multiple declarations and an optional leading comment.
let formatModule
    (moduleName: string)
    (leadingComment: string option)
    (opens: string list)
    (decls: _ list)
    : string =
    if moduleName.LastIndexOf '.' < 0 then
        invalidArg (nameof moduleName) "moduleName must be namespace-qualified (contain a '.')"
    let i = moduleName.LastIndexOf '.'
    let nsName, modName = moduleName.[.. i - 1], moduleName.[i + 1 ..]
    let oak =
        Oak() {
            Namespace nsName {
                Module modName {
                    for o in opens do
                        Open o
                    for d in decls do
                        d
                }
            }
        }
    let body = oak |> Gen.mkOak |> Gen.run
    match leadingComment with
    | None -> body
    | Some c -> c + "\n" + body
```

> Implementer notes, in priority order:
> 1. The `Module` CE must accept both `unionDecl`'s widget (a `Union` type decl) and `matchFunction`'s widget (a `Function` binding) in the same `for d in decls do d` loop. If the two builder return-types differ such that one list can't hold both, make `unionDecl`/`matchFunction` return the common module-declaration widget type the CE yields (check the type Fabulous.AST's `Module` CE consumes). The round-trip test's exact output is the contract.
> 2. `leadingComment`: the constant prepend above is acceptable (a comment is metadata, not assembled F# *code*). If Fabulous.AST exposes a clean module-level XML/trivia comment API you prefer, use it — but the `// <auto-generated>` form is a line comment, and the prepend satisfies the test. Do not assemble any *code* via string concatenation.
> 3. Generic-type rendering: `typedefof<TicTacToe.Game<_>>` must survive `rawExpr` verbatim (verified). Confirm via the test.

- [ ] **Step 4: Run — passes.** Same filter as Step 2. Expected: PASS (the 3 Plan-1 AstRender tests + the 2 new).

- [ ] **Step 5: Fantomas + commit.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && dotnet fantomas src/Frank.Cli.Core/AstRender.fs test/Frank.Cli.Core.Tests/AstRenderTests.fs`

```bash
git add src/Frank.Cli.Core/AstRender.fs test/Frank.Cli.Core.Tests/AstRenderTests.fs
git commit -m "feat(cli): AstRender — union decl + match-function builders (Fabulous.AST)"
```

---

### Task 2: Migrate `SemanticModelEmitter` to `AstRender`

**Files:**
- Modify: `src/Frank.Cli.Core/SemanticModelEmitter.fs`
- Modify: `test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs`

**Interfaces:**
- Consumes: `AstRender.{rawExpr,appExpr,strExpr,unionDecl,matchFunction,formatModule}` (Task 1); `FcsTypecheck.typecheckTwoSources`.
- Produces: `SemanticModelEmitter.emit` unchanged (`moduleName -> registry -> lock -> Result<string,string>`); internal `projectMapped : ResolvedModel -> MappedResource list`.

- [ ] **Step 1: Write the failing tier-1 projection test.** In `SemanticModelEmitterTests.fs` add (reuse the file's existing fixture registry/lock + `okOrFail`):

```fsharp
test "projectMapped yields class-mapped resources with unwrapped ClassIri (tier 1)" {
    let model = ResolvedModel.build probeRegistry probeLock |> okOrFail
    let mapped = SemanticModelEmitter.projectMapped model
    Expect.isNonEmpty mapped "at least one class-mapped resource"
    Expect.contains (mapped |> List.map (fun m -> m.LocalName)) "Move" "Move mapped"
    Expect.contains (mapped |> List.map (fun m -> m.ClassIri.AbsoluteUri)) "https://schema.org/MoveAction" "ClassIri unwrapped"
}
```

(Use the real fixture/type names already in the file. `MappedResource` and `projectMapped` become `internal`; `InternalsVisibleTo Tests` is already granted.)

- [ ] **Step 2: Run — fails** (`projectMapped` undefined).

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "tier 1"`

- [ ] **Step 3: Rewrite `SemanticModelEmitter.fs`.** Keep `MappedResource`, `toMapped`, `clrTypeExpr`, `camel` (make `MappedResource` + a new `projectMapped` internal). Delete `renderDuDecl`/`renderIriMatch`/`renderClrTypeMatch`/`renderCaseMatch`/`assembleModule`. Replace with:

```fsharp
let internal projectMapped (model: ResolvedModel) : MappedResource list =
    model.Resources |> List.choose toMapped

let private autoGenComment =
    "// <auto-generated> Anti-drift guard: compiled WITH the domain so renaming/removing a mapped type breaks the build. Not consumed at runtime. </auto-generated>"

let private duDecl (mapped: MappedResource list) =
    AstRender.unionDecl "SemanticResource" (mapped |> List.map (fun m -> m.LocalName))

let private iriFn (mapped: MappedResource list) =
    AstRender.matchFunction "iri" "r" "SemanticResource" "System.Uri"
        (mapped |> List.map (fun m ->
            m.LocalName, AstRender.appExpr "System.Uri" (AstRender.strExpr m.ClassIri.AbsoluteUri)))

let private clrTypeFn (mapped: MappedResource list) =
    AstRender.matchFunction "clrType" "r" "SemanticResource" "System.Type"
        (mapped |> List.map (fun m ->
            m.LocalName, AstRender.rawExpr (clrTypeExpr m.FSharpType m.GenericArity)))

// <type>CaseIri functions: one per union resource with cases. Pattern strings: nullary "Name",
// payload "Name _"; append a "_" wildcard clause → None when coverage is partial.
let private caseFns (mapped: MappedResource list) =
    mapped |> List.choose (fun m ->
        if m.Cases.IsEmpty then None
        else
            let fnName = camel m.LocalName + "CaseIri"
            let mappedClauses =
                m.Cases |> List.map (fun c ->
                    let pat = if c.IsNullary then c.CaseName else c.CaseName + " _"
                    pat, AstRender.appExpr "Some" (AstRender.appExpr "System.Uri" (AstRender.strExpr c.Iri.AbsoluteUri)))
            let clauses =
                if m.Cases.Length = m.UnionCaseCount then mappedClauses
                else mappedClauses @ [ "_", AstRender.rawExpr "None" ]
            Some(AstRender.matchFunction fnName "x" m.FSharpType "System.Uri option" clauses))

let emit (moduleName: string) (registry: VocabularyRegistry) (lock: LockFile) : Result<string, string> =
    if String.IsNullOrWhiteSpace moduleName then
        invalidArg (nameof moduleName) "moduleName must not be empty"
    match ResolvedModel.build registry lock with
    | Error e -> Error e
    | Ok model ->
        let mapped = projectMapped model
        if mapped.IsEmpty then
            Error "no class-mapped resources to generate a semantic model"
        else
            let decls = duDecl mapped :: iriFn mapped :: clrTypeFn mapped :: caseFns mapped
            Ok(AstRender.formatModule moduleName (Some autoGenComment) [] decls)
```

> Implementer notes:
> - `decls` mixes the DU decl, the `iri`/`clrType` functions, and the `<type>CaseIri` functions — all must be the same module-declaration widget type `formatModule` accepts (Task 1 settled this). If `caseFns` returns `[]` (no union resources), `decls` is still the DU + 2 functions — fine.
> - `clrTypeExpr m.FSharpType m.GenericArity` already returns the exact `typeof<…>`/`typedefof<…<_>>` string — feed it to `rawExpr` unchanged. Do NOT reimplement it.
> - The `Some(System.Uri "…")` rendering differs cosmetically from the old `Some(System.Uri("…"))`; the anti-drift FCS gate typechecks (formatting-agnostic), so it still passes. Reconcile any substring test that asserted the old `System.Uri(` exact form against the new output (print it) — never weaken the anti-drift gate or delete a test.

- [ ] **Step 4: Run tier-1 — passes.** Same filter as Step 2.

- [ ] **Step 5: Run the full SemanticModel suite — anti-drift gate still green.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ --filter "SemanticModel"`
The anti-drift test (rename `Game`→`GameX` in the domain → emitted module has compile errors) MUST still pass — it proves the emitted `typeof<…>`/case-constructor references are real. Reconcile only formatting-coupled substring assertions against the new Fantomas output; print the emitted source to see reality. Do NOT weaken the anti-drift or `no urn:frank:` assertions.

- [ ] **Step 6: Full Cli.Core suite + fantomas + commit.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ && dotnet fantomas --check src/Frank.Cli.Core/SemanticModelEmitter.fs src/Frank.Cli.Core/AstRender.fs`
Expected: full suite PASS; fantomas clean.

```bash
git add src/Frank.Cli.Core/SemanticModelEmitter.fs test/Frank.Cli.Core.Tests/SemanticModelEmitterTests.fs
git commit -m "refactor(cli): SemanticModelEmitter emits via AstRender; typed-projection test; anti-drift gate preserved"
```

---

## Self-Review

- **Spec coverage:** Plan 2 covers spec AC #1 (no string assembly — SemanticModel) and preserves the anti-drift guard (#5 drift detection for SemanticModel). It does NOT introduce data DUs / term layer (those are Plan 3 LinkedData / Plan 4 Validation, per YAGNI — SemanticModel emits its own DU + functions, no `ShapeDecl`/`OntologyDecl`).
- **Placeholder scan:** none — all code is real (run-verified API). The `<module-decl widget>` type is left for the implementer to pin under the round-trip test (the verified builder calls are given); this is API-binding, not a deferral.
- **Type consistency:** `AstRender` additions (`rawExpr`/`appExpr`/`unionDecl`/`matchFunction`/`formatModule`) used consistently in Task 2. `projectMapped`/`MappedResource` consistent. `clrTypeExpr` reused, not redefined.

## Next

- **Plan 3:** LinkedData typed `OntologyDecl` + interpreter in `Frank.LinkedData` (adds `Frank.Semantic` triple helper + `OntologyDecl`).
- **Plan 4:** Validation fresh build (`ShapeDecl` + `Shapes.toShapesGraph`; cherry-pick `enrichTypes`).
