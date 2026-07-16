module Frank.Cli.Core.LinkedDataEmitter

open System
open Frank.Semantic

/// Resolve the external base IRIs for the @context from the model's Using set and Prefixes map.
/// Iterates Set.toList (ascending) — identical order to the old buildContext loop.
/// Returns Error if any using prefix is not in Prefixes.
val internal contextBases: model: ResolvedModel -> Result<Uri list, string>

val internal projectOntology: model: ResolvedModel -> OntologyDecl

/// Emit a GeneratedLinkedData F# module from a lock file and vocabulary registry.
///
/// moduleName — the F# module name to emit
/// registry   — the VocabularyRegistry providing prefix→URI mappings
/// lock       — the resolved lock file
///
/// Returns Ok with the F# source string, or Error if any IRI references an unknown prefix.
val emit: moduleName: string -> registry: VocabularyRegistry -> lock: LockFile.LockFile -> Result<string, string>
