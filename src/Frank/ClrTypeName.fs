namespace Frank

/// Normalizes a live, reflection-derived `System.Type.FullName` to the SAME convention
/// DiscoveryEmitter's codegen bakes into `RequestClrTypeName` (FSharpEntity.TryFullName,
/// via Frank.Cli.Core's Extractor/ConventionEngine/ResolvedModel pipeline). Two
/// independent CLR-vs-FCS conventions disagree without this:
///  - Module nesting: CLR reflection separates a type nested in a compiled F# module with
///    '+' (e.g. a record declared inside `module TicTacToe.Model` compiles as the nested
///    type "TicTacToe.Model+MoveRequest"), while FCS's symbolic FullName represents the
///    exact same source-level qualified name with '.' throughout
///    ("TicTacToe.Model.MoveRequest") — F# source syntax never distinguishes
///    module-nesting from namespace-nesting.
///  - Closed generics: CLR reflection appends a backtick-arity marker AND, for a closed
///    generic, bracketed assembly-qualified type arguments (e.g.
///    "Outer+Wrapper`1[[Outer+Payload, Asm, Version=..., Culture=neutral,
///    PublicKeyToken=null]]"), while FCS's TryFullName for the same (open, unapplied)
///    generic type definition keeps the backtick-arity marker but never the bracketed
///    type arguments ("Outer.Wrapper`1") — confirmed via a `dotnet fsi` probe against
///    FSharpChecker.ParseAndCheckProject, not assumed.
/// Without this normalization the two conventions never compare equal: a live map key
/// (built from reflection) silently fails to match any module-nested or generic
/// RequestClrTypeName, so reconciliation no-ops and the codegen default survives
/// unreconciled — the exact defect a coincidentally-correct codegen default can mask.
/// Shared by Frank.Discovery.DiscoveryMiddleware and Frank.Provenance.ProvenanceMiddleware
/// (#400 /simplify: one implementation, not two independent copies — constitution rule 8).
[<RequireQualifiedAccess>]
module ClrTypeName =

    /// Strip a closed generic's bracketed, assembly-qualified type-argument list (if
    /// present — everything from the first '[' onward) and normalize '+' module/type
    /// nesting to '.' — see module doc for the two independent CLR-vs-FCS conventions
    /// this reconciles.
    let normalizeFullName (fullName: string) : string =
        let withoutGenericArgs =
            match fullName.IndexOf('[') with
            | -1 -> fullName
            | i -> fullName.[.. i - 1]

        withoutGenericArgs.Replace('+', '.')
