module Frank.Cli.Core.ValidationEmitter

open System
open Frank.Semantic

/// Project an enriched ResolvedModel to static ShapeDecl list (external-vocab fields only)
/// and host-relative property tuples (app-owned fields).
val internal projectShapes: model: ResolvedModel -> Result<ShapeDecl list * (Uri * string * string option) list, string>

/// Emit a GeneratedValidation F# module from a lock file, vocabulary registry, and
/// FCS-extracted type shapes.
///
/// moduleName  — the F# module name to emit
/// registry    — the VocabularyRegistry supplying prefix→URI mappings
/// lock        — the resolved lock file
/// typesByName — FCS-extracted TypeInfo map keyed by FullName
///
/// Returns Ok with the F# source string, or Error if any shaped field has an empty TypeName.
val emit:
    moduleName: string ->
    registry: VocabularyRegistry ->
    lock: LockFile.LockFile ->
    typesByName: Map<string, TypeInfo> ->
        Result<string, string>
