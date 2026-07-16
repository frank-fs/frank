module Frank.Cli.Core.SemanticModelEmitter

open System
open Frank.Semantic

/// A class-mapped resource with its ClassIri already unwrapped.
/// Only resources where ClassIri.IsSome are promoted to this form.
type internal MappedResource =
    { LocalName: string
      ClassIri: Uri
      FSharpType: string
      GenericArity: int
      Cases: ResolvedCase list
      UnionCaseCount: int }

val internal projectMapped: model: ResolvedModel -> MappedResource list

/// Emit a GeneratedSemantics F# module from a lock file and vocabulary registry.
///
/// moduleName — the F# module name to emit
/// registry   — the VocabularyRegistry providing prefix→URI mappings
/// lock       — the resolved lock file
///
/// Returns Ok with the F# source string, or Error if no class-mapped resources exist
/// or if ResolvedModel.build fails.
val emit: moduleName: string -> registry: VocabularyRegistry -> lock: LockFile.LockFile -> Result<string, string>
