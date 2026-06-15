module Frank.Cli.Core.SemanticModelEmitter

open System
open Frank.Semantic
open Frank.Semantic.LockFile

// ── Type expression helpers ───────────────────────────────────────────────────

/// Build the F# type expression for a clrType match arm.
/// GenericArity = 0 → typeof<FSharpType>
/// GenericArity = N → typedefof<BaseType<_,...>> with N underscores (backtick suffix stripped)
let private clrTypeExpr (fsharpType: string) (genericArity: int) : string =
    if genericArity = 0 then
        "typeof<" + fsharpType + ">"
    else
        let baseName =
            match fsharpType.IndexOf('`') with
            | -1 -> fsharpType
            | idx -> fsharpType.[.. idx - 1]

        let underscores = List.replicate genericArity "_" |> String.concat ","
        "typedefof<" + baseName + "<" + underscores + ">>"

// ── Source rendering ──────────────────────────────────────────────────────────

/// Render the SemanticResource DU type declaration.
let private renderDuDecl (resources: ResolvedResource list) : string =
    let cases =
        resources |> List.map (fun r -> "    | " + r.LocalName) |> String.concat "\n"

    "type SemanticResource =\n" + cases

/// Render the iri function match expression.
let private renderIriMatch (resources: ResolvedResource list) : string =
    let arms =
        resources
        |> List.map (fun r -> "    | " + r.LocalName + " -> \"" + r.ClassIri.Value.AbsoluteUri + "\"")
        |> String.concat "\n"

    "let iri (r: SemanticResource) : string =\n    match r with\n" + arms

/// Render the clrType function match expression.
let private renderClrTypeMatch (resources: ResolvedResource list) : string =
    let arms =
        resources
        |> List.map (fun r -> "    | " + r.LocalName + " -> " + clrTypeExpr r.FSharpType r.GenericArity)
        |> String.concat "\n"

    "let clrType (r: SemanticResource) : System.Type =\n    match r with\n" + arms

/// Assemble the full F# module source string.
let private assembleModule (moduleName: string) (resources: ResolvedResource list) : string =
    String.concat
        "\n"
        [ $"module {moduleName}"
          ""
          renderDuDecl resources
          ""
          renderIriMatch resources
          ""
          renderClrTypeMatch resources
          "" ]

// ── Public API ────────────────────────────────────────────────────────────────

/// Emit a SemanticModel F# module from a vocabulary registry and lock file.
///
/// moduleName — the F# module name to emit (e.g. "TicTacToe.GeneratedSemanticModel")
/// registry   — the VocabularyRegistry providing prefix→URI mappings
/// lock       — the resolved lock file
///
/// Returns Ok with the F# source string, or Error if no class-mapped resources exist
/// or if ResolvedModel.build fails.
let emit (moduleName: string) (registry: VocabularyRegistry) (lock: LockFile) : Result<string, string> =
    if String.IsNullOrWhiteSpace moduleName then
        invalidArg (nameof moduleName) "moduleName must not be empty"

    match ResolvedModel.build registry lock with
    | Error e -> Error e
    | Ok model ->
        let classMapped = model.Resources |> List.filter (fun r -> r.ClassIri.IsSome)

        if classMapped.IsEmpty then
            Error "no class-mapped resources to generate a semantic model"
        else
            Ok(assembleModule moduleName classMapped)
